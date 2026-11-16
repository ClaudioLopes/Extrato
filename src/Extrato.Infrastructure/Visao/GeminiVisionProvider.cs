using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Extrato.Application;
using Extrato.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Extrato.Infrastructure.Visao;

public class GeminiOptions
{
    public const string Secao = "Gemini";
    public string? ApiKey { get; set; }
    public string Modelo { get; set; } = "gemini-3.5-flash-lite";
    /// <summary>
    /// Tentados em ordem quando o modelo anterior está sobrecarregado (503 depois das tentativas) ou não existe (404).
    /// No plano grátis, modelo recém-lançado costuma ficar sobrecarregado.
    /// </summary>
    public List<string> ModelosReserva { get; set; } = ["gemini-3.8-flash"];
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
    /// <summary>Tentativas no último modelo da lista; os anteriores têm 2, para passar logo ao reserva.</summary>
    public int MaxTentativas { get; set; } = 2;
    public int TimeoutSegundos { get; set; } = 30;
}

/// <summary>
/// Gemini (tier grátis) atrás de IVisionProvider. Saída estruturada (JSON schema na requisição),
/// retry com backoff exponencial em 429/503. Atenção (seção 2.1): no tier grátis o Google pode usar
/// o conteúdo para melhorar produtos. Use só para foto de prato, nunca para foto do corpo.
/// </summary>
public class GeminiVisionProvider(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiVisionProvider> logger) : IVisionProvider
{
    private readonly GeminiOptions _opt = options.Value;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Nome => $"Gemini ({_opt.Modelo})";

    // tier grátis: o Google pode usar o conteúdo enviado (seção 2.1)
    public bool AceitaDadosSensiveis => false;

    public Task<AnaliseCorpoResultado> AnalisarCorpoAsync(Stream imagem, string? descricao, Sexo sexo, CancellationToken ct) =>
        throw new VisionIndisponivelException("Foto do corpo não é enviada ao Gemini (tier grátis). Use o Claude ou só as medidas.");

    public const string PromptPrato =
        "Você analisa fotos de refeições brasileiras. Liste cada alimento visível com nome em português, " +
        "forma de preparo e gramas estimadas. Considere um prato raso de ~26 cm como referência. " +
        "Se houver descrição do usuário, ela prevalece sobre a imagem em quantidade e identificação. " +
        "Inclua óleo, molhos e acompanhamentos prováveis como itens separados, com confiança baixa. " +
        RegrasPorcao + "Não calcule calorias. Responda somente no JSON do schema.";

    /// <summary>Pós e bebidas preparadas: a IA confundia o copo de achocolatado com 200 g de pó.</summary>
    public const string RegrasPorcao =
        "Gramas são do alimento como está na tabela: bebida preparada vira dois itens, o líquido (leite, água) " +
        "e o pó (achocolatado ~20 g por copo, whey ~30 g por dose, creatina 3–5 g, café solúvel ~2 g). " +
        "Líquidos: 1 ml ≈ 1 g; copo comum 200 ml, lata 350 ml. Medidas caseiras: colher de sopa ~15 g " +
        "(de arroz ~25 g), concha de feijão ~80 g, fatia de queijo ~20 g, pão francês 50 g, ovo 50 g. ";

    public const string PromptDescricao =
        "Você converte a descrição de uma refeição brasileira, escrita pelo usuário, em itens com nome em português, " +
        "forma de preparo e gramas. Use as quantidades informadas; sem quantidade, use a porção típica de uma pessoa " +
        "e confiança baixa. Nomes genéricos como na Tabela TACO (ex.: \"Leite, de vaca, integral\"); suplementos " +
        "pelo nome comum (whey protein, creatina). " + RegrasPorcao +
        "Não calcule calorias. Responda somente no JSON do schema.";

    public const string PromptAparelho =
        "Você identifica aparelhos de musculação em fotos de academia. Diga o exercício principal, o grupo muscular " +
        "e o tipo de carga (Pilha, Anilha, Halter ou PesoCorporal). Se houver qualquer pessoa no enquadramento, " +
        "marque haPessoas como true. Responda somente no JSON do schema.";

    private static readonly JsonObject SchemaPrato = JsonNode.Parse("""
    {
      "type": "OBJECT",
      "properties": {
        "itens": {
          "type": "ARRAY",
          "items": {
            "type": "OBJECT",
            "properties": {
              "nome": { "type": "STRING" },
              "preparo": { "type": "STRING" },
              "gramas": { "type": "NUMBER" },
              "confianca": { "type": "NUMBER" },
              "observacao": { "type": "STRING" }
            },
            "required": ["nome", "gramas", "confianca"]
          }
        },
        "alertas": { "type": "ARRAY", "items": { "type": "STRING" } }
      },
      "required": ["itens"]
    }
    """)!.AsObject();

    private static readonly JsonObject SchemaAparelho = JsonNode.Parse("""
    {
      "type": "OBJECT",
      "properties": {
        "exercicio": { "type": "STRING" },
        "grupoMuscular": { "type": "STRING" },
        "tipoCarga": { "type": "STRING", "enum": ["Pilha", "Anilha", "Halter", "PesoCorporal"] },
        "haPessoas": { "type": "BOOLEAN" },
        "alertas": { "type": "ARRAY", "items": { "type": "STRING" } }
      },
      "required": ["exercicio", "grupoMuscular", "tipoCarga", "haPessoas"]
    }
    """)!.AsObject();

    private static readonly JsonObject SchemaAtividade = JsonNode.Parse("""
    {
      "type": "OBJECT",
      "properties": {
        "ehResumoDeAtividade": { "type": "BOOLEAN" },
        "tipo": { "type": "STRING", "enum": ["Natacao", "Volei", "Corrida", "Caminhada", "Ciclismo", "Futebol", "Outra"] },
        "descricao": { "type": "STRING" },
        "dataHora": { "type": "STRING" },
        "duracaoSegundos": { "type": "INTEGER" },
        "kcalAtivas": { "type": "INTEGER" },
        "kcalTotais": { "type": "INTEGER" },
        "distanciaM": { "type": "INTEGER" },
        "fcMedia": { "type": "INTEGER" },
        "alertas": { "type": "ARRAY", "items": { "type": "STRING" } }
      },
      "required": ["ehResumoDeAtividade", "tipo", "dataHora", "duracaoSegundos", "kcalAtivas", "kcalTotais"]
    }
    """)!.AsObject();

    /// <summary>Print de resumo (números da pulseira, sem rosto nem corpo): pode ir ao tier grátis, como a foto do prato.</summary>
    public async Task<AnaliseAtividadeResultado> AnalisarAtividadeAsync(Stream imagem, CancellationToken ct)
    {
        var json = await GerarAsync(AtividadeJson.Prompt, SchemaAtividade, imagem, AtividadeJson.Texto(DateTime.Now), ct);
        return Desserializar<AtividadeJson>(json).ParaResultado();
    }

    private record ItemJson(string? Nome, string? Preparo, decimal Gramas, decimal Confianca, string? Observacao);
    private record PratoJson(List<ItemJson>? Itens, List<string>? Alertas);
    private record AparelhoJson(string? Exercicio, string? GrupoMuscular, string? TipoCarga, bool HaPessoas, List<string>? Alertas);

    public async Task<AnalisePratoResultado> AnalisarPratoAsync(Stream imagem, string? descricao, TipoRefeicao tipo, CancellationToken ct)
    {
        var texto = $"Refeição: {tipo}." + (string.IsNullOrWhiteSpace(descricao) ? " Sem descrição do usuário." : $" Descrição do usuário: \"{descricao}\".");
        return Prato(await GerarAsync(PromptPrato, SchemaPrato, imagem, texto, ct));
    }

    public async Task<AnalisePratoResultado> AnalisarDescricaoAsync(string descricao, TipoRefeicao tipo, CancellationToken ct) =>
        Prato(await GerarAsync(PromptDescricao, SchemaPrato, null, $"Refeição: {tipo}. Descrição do usuário: \"{descricao}\".", ct));

    private static AnalisePratoResultado Prato(string json)
    {
        var r = Desserializar<PratoJson>(json);
        if (r.Itens is null) throw new VisionIndisponivelException("JSON sem a lista de itens.");
        return new AnalisePratoResultado(
            r.Itens.Select(i => new ItemIdentificado(i.Nome ?? "", i.Preparo, i.Gramas, i.Confianca, i.Observacao)).ToList(),
            r.Alertas ?? []);
    }

    public async Task<AnaliseAparelhoResultado> AnalisarAparelhoAsync(Stream imagem, string? nome, CancellationToken ct)
    {
        var texto = string.IsNullOrWhiteSpace(nome) ? "Sem nome informado." : $"Nome informado pelo usuário: \"{nome}\".";
        var json = await GerarAsync(PromptAparelho, SchemaAparelho, imagem, texto, ct);
        var r = Desserializar<AparelhoJson>(json);
        return new AnaliseAparelhoResultado(r.Exercicio, r.GrupoMuscular, r.TipoCarga, r.HaPessoas, r.Alertas ?? []);
    }

    private static T Desserializar<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw new JsonException("vazio"); }
        catch (JsonException ex) { throw new VisionIndisponivelException("A IA devolveu JSON inválido.", ex); }
    }

    /// <summary>Mensagem de erro do Google (error.message), sem a imagem; no máximo 300 caracteres.</summary>
    private static async Task<string> MensagemErroAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var corpo = await resp.Content.ReadAsStringAsync(ct);
            var msg = JsonNode.Parse(corpo)?["error"]?["message"]?.GetValue<string>() ?? corpo;
            return msg.Length > 300 ? msg[..300] : msg;
        }
        catch (Exception) { return "(sem detalhes)"; }
    }

    /// <param name="imagem">null: só texto (descrição da refeição).</param>
    private async Task<string> GerarAsync(string sistema, JsonObject schema, Stream? imagem, string texto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey)) throw new VisionIndisponivelException("Chave do Gemini não configurada.");

        var partes = new JsonArray();
        if (imagem is not null)
        {
            using var ms = new MemoryStream();
            await imagem.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            partes.Add(new JsonObject
            {
                ["inline_data"] = new JsonObject { ["mime_type"] = Imagem.SanitizadorImagem.MimeDe(bytes), ["data"] = Convert.ToBase64String(bytes) }
            });
        }
        partes.Add(new JsonObject { ["text"] = texto });

        var corpo = new JsonObject
        {
            ["system_instruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = sistema }) },
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = partes }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = schema.DeepClone(),
                ["temperature"] = 0.2
            }
        };

        // modelo principal e reservas: sobrecarregado (503) ou inexistente (404) passa para o próximo
        var modelos = new[] { _opt.Modelo }.Concat(_opt.ModelosReserva).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList();
        for (var i = 0; ; i++)
        {
            var ultimo = i == modelos.Count - 1;
            try
            {
                // antes do último modelo: menos tentativas e limite de tempo menor, para passar logo ao reserva
                return await GerarComModeloAsync(modelos[i], corpo, ultimo ? Math.Max(1, _opt.MaxTentativas) : 2,
                    ultimo ? _opt.TimeoutSegundos : Math.Min(_opt.TimeoutSegundos, 15), ct);
            }
            catch (ModeloIndisponivelException ex) when (!ultimo)
            {
                logger.LogWarning("Gemini {Modelo} indisponível ({Motivo}); tentando {Proximo}.", modelos[i], ex.Message, modelos[i + 1]);
            }
        }
    }

    /// <summary>503 depois das tentativas, tempo esgotado ou 404 (modelo inexistente): vale tentar o modelo reserva.</summary>
    private sealed class ModeloIndisponivelException(string mensagem) : VisionIndisponivelException(mensagem);

    private async Task<string> GerarComModeloAsync(string modelo, JsonObject corpo, int maxTentativas, int timeoutSegundos, CancellationToken ct)
    {
        var url = $"models/{modelo}:generateContent";
        for (var tentativa = 1; ; tentativa++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo) };
            req.Headers.Add("x-goog-api-key", _opt.ApiKey);

            HttpResponseMessage resp;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSegundos));
            try
            {
                resp = await http.SendAsync(req, timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Gemini {Modelo}: sem resposta em {Segundos}s.", modelo, timeoutSegundos);
                throw new ModeloIndisponivelException("Tempo esgotado ao consultar o Gemini. Tente de novo em alguns minutos ou registre manualmente.");
            }
            catch (HttpRequestException ex)
            {
                throw new VisionIndisponivelException("Falha de rede ao consultar o Gemini.", ex);
            }

            using (resp)
            {
                if (resp.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    if (tentativa >= maxTentativas)
                    {
                        var erro = await MensagemErroAsync(resp, ct);
                        logger.LogWarning("Gemini {Modelo} HTTP {Status} após {Tentativas} tentativa(s): {Erro}", modelo, (int)resp.StatusCode, tentativa, erro);
                        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                            throw new VisionIndisponivelException("Cota grátis do Gemini esgotada por agora (HTTP 429). Tente mais tarde ou registre manualmente.");
                        throw new ModeloIndisponivelException("Gemini sobrecarregado (HTTP 503). Tente de novo em alguns minutos ou registre manualmente.");
                    }
                    var espera = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, tentativa - 1));
                    espera += TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250)); // jitter
                    logger.LogInformation("Gemini {Modelo} HTTP {Status}; nova tentativa {Tentativa} em {Espera}", modelo, (int)resp.StatusCode, tentativa + 1, espera);
                    await Task.Delay(espera, ct);
                    continue;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    var erro = await MensagemErroAsync(resp, ct);
                    logger.LogWarning("Gemini {Modelo} HTTP {Status}: {Erro}", modelo, (int)resp.StatusCode, erro);
                    if (resp.StatusCode == HttpStatusCode.NotFound)
                        throw new ModeloIndisponivelException($"Modelo {modelo} não encontrado no Gemini (HTTP 404).");
                    throw new VisionIndisponivelException($"Gemini respondeu HTTP {(int)resp.StatusCode}.");
                }

                var resposta = await resp.Content.ReadFromJsonAsync<JsonObject>(ct);
                // modelos com raciocínio podem trazer partes de "pensamento" antes do texto: junta só o texto da resposta
                var partes = resposta?["candidates"]?[0]?["content"]?["parts"]?.AsArray() ?? [];
                var textoJson = string.Concat(partes
                    .Where(p => p?["thought"]?.GetValue<bool>() != true)
                    .Select(p => p?["text"]?.GetValue<string>()));
                if (string.IsNullOrWhiteSpace(textoJson))
                    throw new VisionIndisponivelException("Gemini não devolveu conteúdo (possível bloqueio de segurança).");
                return textoJson;
            }
        }
    }
}
