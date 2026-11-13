using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Extrato.Application;
using Extrato.Domain;
using Extrato.Infrastructure.Imagem;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Extrato.Infrastructure.Visao;

public class ClaudeOptions
{
    public const string Secao = "Claude";
    /// <summary>Chave da API (console da Anthropic). Sem ela, usa ANTHROPIC_API_KEY do ambiente.</summary>
    public string? ApiKey { get; set; }
    public string Modelo { get; set; } = "claude-opus-5-5";
    /// <summary>low | medium | high. Identificar alimentos é tarefa simples: low mantém a resposta rápida.</summary>
    public string Esforco { get; set; } = "low";
    public int MaxTentativas { get; set; } = 3;
    public int TimeoutSegundos { get; set; } = 60;
}

/// <summary>
/// Claude (API da Anthropic) atrás de IVisionProvider. Saída estruturada por JSON Schema,
/// retries de 429/5xx feitos pelo SDK, e fallback de modelo no servidor quando a resposta é recusada.
/// </summary>
public class ClaudeVisionProvider : IVisionProvider
{
    private readonly ClaudeOptions _opt;
    private readonly AnthropicClient _client;
    private readonly ILogger<ClaudeVisionProvider> _logger;

    public ClaudeVisionProvider(IOptions<ClaudeOptions> options, ILogger<ClaudeVisionProvider> logger)
    {
        _opt = options.Value;
        _logger = logger;
        _client = string.IsNullOrWhiteSpace(_opt.ApiKey)
            ? new AnthropicClient { MaxRetries = _opt.MaxTentativas, Timeout = TimeSpan.FromSeconds(_opt.TimeoutSegundos) }
            : new AnthropicClient { ApiKey = _opt.ApiKey, MaxRetries = _opt.MaxTentativas, Timeout = TimeSpan.FromSeconds(_opt.TimeoutSegundos) };
    }

    public string Nome => $"Claude ({_opt.Modelo})";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, JsonElement> SchemaPrato = Schema(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "itens", "alertas" },
        properties = new
        {
            itens = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "nome", "preparo", "gramas", "confianca", "observacao" },
                    properties = new
                    {
                        nome = new { type = "string" },
                        preparo = new { type = "string" },
                        gramas = new { type = "number" },
                        confianca = new { type = "number" },
                        observacao = new { type = "string" }
                    }
                }
            },
            alertas = new { type = "array", items = new { type = "string" } }
        }
    });

    private static readonly Dictionary<string, JsonElement> SchemaAparelho = Schema(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "exercicio", "grupoMuscular", "tipoCarga", "haPessoas", "alertas" },
        properties = new
        {
            exercicio = new { type = "string" },
            grupoMuscular = new { type = "string" },
            tipoCarga = new { type = "string", @enum = new[] { "Pilha", "Anilha", "Halter", "PesoCorporal" } },
            haPessoas = new { type = "boolean" },
            alertas = new { type = "array", items = new { type = "string" } }
        }
    });

    private record ItemJson(string? Nome, string? Preparo, decimal Gramas, decimal Confianca, string? Observacao);
    private record PratoJson(List<ItemJson>? Itens, List<string>? Alertas);
    private record AparelhoJson(string? Exercicio, string? GrupoMuscular, string? TipoCarga, bool HaPessoas, List<string>? Alertas);

    public async Task<AnalisePratoResultado> AnalisarPratoAsync(Stream imagem, string? descricao, TipoRefeicao tipo, CancellationToken ct)
    {
        var texto = $"Refeição: {tipo}." + (string.IsNullOrWhiteSpace(descricao)
            ? " Sem descrição do usuário."
            : $" Descrição do usuário: \"{descricao}\".");
        var json = await GerarAsync(GeminiVisionProvider.PromptPrato, SchemaPrato, imagem, texto, ct);
        var r = Desserializar<PratoJson>(json);
        if (r.Itens is null) throw new VisionIndisponivelException("JSON sem a lista de itens.");
        return new AnalisePratoResultado(
            r.Itens.Select(i => new ItemIdentificado(i.Nome ?? "", Vazio(i.Preparo), i.Gramas, i.Confianca, Vazio(i.Observacao))).ToList(),
            r.Alertas ?? []);
    }

    public async Task<AnaliseAparelhoResultado> AnalisarAparelhoAsync(Stream imagem, string? nome, CancellationToken ct)
    {
        var texto = string.IsNullOrWhiteSpace(nome) ? "Sem nome informado." : $"Nome informado pelo usuário: \"{nome}\".";
        var json = await GerarAsync(GeminiVisionProvider.PromptAparelho, SchemaAparelho, imagem, texto, ct);
        var r = Desserializar<AparelhoJson>(json);
        return new AnaliseAparelhoResultado(r.Exercicio, r.GrupoMuscular, r.TipoCarga, r.HaPessoas, r.Alertas ?? []);
    }

    private async Task<string> GerarAsync(string sistema, Dictionary<string, JsonElement> schema, Stream imagem, string texto, CancellationToken ct)
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await imagem.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }
        var png = SanitizadorImagem.MimeDe(bytes) == "image/png";

        var parametros = new MessageCreateParams
        {
            Model = _opt.Modelo,
            MaxTokens = 4096,
            Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new Default(),
            System = sistema,
            OutputConfig = new BetaOutputConfig
            {
                Effort = _opt.Esforco.ToLowerInvariant() switch
                {
                    "medium" => Effort.Medium,
                    "high" => Effort.High,
                    _ => Effort.Low
                },
                Format = new BetaJsonOutputFormat { Schema = schema },
            },
            Messages =
            [
                new BetaUserMessageParam
                {
                    Content = new List<BetaContentBlockParam>
                    {
                        new BetaImageBlockParam
                        {
                            Source = new BetaBase64ImageSource
                            {
                                Data = Convert.ToBase64String(bytes),
                                MediaType = png ? MediaType.ImagePng : MediaType.ImageJpeg,
                            },
                        },
                        new BetaTextBlockParam { Text = texto },
                    },
                },
            ],
        };

        BetaMessage resposta;
        try
        {
            resposta = await _client.Beta.Messages.Create(parametros, ct);
        }
        catch (AnthropicRateLimitException)
        {
            throw new VisionIndisponivelException("Limite de uso da API do Claude atingido (HTTP 429). Use o registro manual.");
        }
        catch (Anthropic5xxException ex)
        {
            throw new VisionIndisponivelException("API do Claude indisponível no momento.", ex);
        }
        catch (AnthropicApiException ex)
        {
            // 400/401/403 etc.: configuração ou requisição; não logar o conteúdo (contém a imagem)
            _logger.LogWarning("Claude respondeu erro {Tipo}", ex.GetType().Name);
            throw new VisionIndisponivelException("A API do Claude recusou a requisição (verifique a chave e o modelo).", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new VisionIndisponivelException("Falha de rede ao consultar o Claude.", ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new VisionIndisponivelException("Tempo esgotado ao consultar o Claude.");
        }

        if (resposta.StopReason == "refusal")
            throw new VisionIndisponivelException("O Claude recusou analisar esta imagem.");
        if (resposta.StopReason == "max_tokens")
            throw new VisionIndisponivelException("Resposta do Claude veio incompleta.");

        var textoJson = string.Concat(resposta.Content
            .Select(b => b.TryPickText(out var t) ? t.Text : null)
            .Where(t => t != null));
        if (string.IsNullOrWhiteSpace(textoJson))
            throw new VisionIndisponivelException("O Claude não devolveu conteúdo.");
        return textoJson;
    }

    private static T Desserializar<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw new JsonException("vazio"); }
        catch (JsonException ex) { throw new VisionIndisponivelException("O Claude devolveu JSON inválido.", ex); }
    }

    private static string? Vazio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static Dictionary<string, JsonElement> Schema(object schema) =>
        JsonSerializer.SerializeToElement(schema).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
}
