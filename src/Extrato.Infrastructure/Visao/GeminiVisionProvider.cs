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
    public string Modelo { get; set; } = "gemini-2.5-flash";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
    public int MaxTentativas { get; set; } = 4;
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
        var json = await GerarAsync(PromptPrato, SchemaPrato, imagem, texto, ct);
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

    private async Task<string> GerarAsync(string sistema, JsonObject schema, Stream imagem, string texto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey)) throw new VisionIndisponivelException("Chave do Gemini não configurada.");

        string base64, mime;
        using (var ms = new MemoryStream())
        {
            await imagem.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            mime = Imagem.SanitizadorImagem.MimeDe(bytes);
            base64 = Convert.ToBase64String(bytes);
        }

        var corpo = new JsonObject
        {
            ["system_instruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = sistema }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(
                    new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = mime, ["data"] = base64 } },
                    new JsonObject { ["text"] = texto })
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = schema.DeepClone(),
                ["temperature"] = 0.2
            }
        };

        var url = $"models/{_opt.Modelo}:generateContent";
        for (var tentativa = 1; ; tentativa++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo) };
            req.Headers.Add("x-goog-api-key", _opt.ApiKey);

            HttpResponseMessage resp;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_opt.TimeoutSegundos));
            try
            {
                resp = await http.SendAsync(req, timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new VisionIndisponivelException("Tempo esgotado ao consultar o Gemini.");
            }
            catch (HttpRequestException ex)
            {
                throw new VisionIndisponivelException("Falha de rede ao consultar o Gemini.", ex);
            }

            using (resp)
            {
                if (resp.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    if (tentativa >= _opt.MaxTentativas)
                        throw new VisionIndisponivelException(resp.StatusCode == HttpStatusCode.TooManyRequests
                            ? "Cota do Gemini esgotada (HTTP 429). Use o registro manual."
                            : "Gemini indisponível (HTTP 503).");
                    var espera = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, tentativa - 1));
                    espera += TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250)); // jitter
                    logger.LogInformation("Gemini HTTP {Status}; nova tentativa {Tentativa} em {Espera}", (int)resp.StatusCode, tentativa + 1, espera);
                    await Task.Delay(espera, ct);
                    continue;
                }

                if (!resp.IsSuccessStatusCode)
                    throw new VisionIndisponivelException($"Gemini respondeu HTTP {(int)resp.StatusCode}.");

                var resposta = await resp.Content.ReadFromJsonAsync<JsonObject>(ct);
                var textoJson = resposta?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(textoJson))
                    throw new VisionIndisponivelException("Gemini não devolveu conteúdo (possível bloqueio de segurança).");
                return textoJson;
            }
        }
    }
}
