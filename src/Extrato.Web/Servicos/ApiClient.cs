using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Extrato.Contracts;
using Extrato.Domain;
using Microsoft.AspNetCore.Components.Forms;

namespace Extrato.Web.Servicos;

public class ApiException(string mensagem) : Exception(mensagem);

/// <summary>Estado da sessão compartilhado: o layout decide entre login, primeiro acesso e app.</summary>
public class SessaoEstado
{
    public SessaoUsuarioDto? Atual { get; private set; }
    public event Action? Mudou;

    public void Definir(SessaoUsuarioDto sessao)
    {
        Atual = sessao;
        Mudou?.Invoke();
    }

    /// <summary>A API respondeu 401: o cookie expirou ou foi removido.</summary>
    public void Expirou()
    {
        if (Atual is { Autenticado: true } a) Definir(a with { Autenticado = false, Login = null });
    }
}

/// <summary>Cliente tipado da API. Erros viram ApiException com a mensagem do ProblemDetails.</summary>
public class ApiClient(HttpClient http, SessaoEstado sessao)
{
    public const long TamanhoMaxFoto = 15 * 1024 * 1024;
    public const int LadoMaximoFoto = 1024;

    /// <summary>
    /// Redimensiona (máx. 1024 px) e recodifica em JPEG no próprio navegador via canvas,
    /// o que descarta EXIF/GPS antes do upload. O servidor ainda sanitiza (defesa em profundidade).
    /// </summary>
    private static async Task<StreamContent> PrepararFotoAsync(IBrowserFile foto)
    {
        IBrowserFile arquivo;
        try { arquivo = await foto.RequestImageFileAsync("image/jpeg", LadoMaximoFoto, LadoMaximoFoto); }
        catch (Exception) { arquivo = foto; } // navegador sem suporte: o servidor valida e limpa
        var conteudo = new StreamContent(arquivo.OpenReadStream(TamanhoMaxFoto));
        conteudo.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrEmpty(arquivo.ContentType) ? "image/jpeg" : arquivo.ContentType);
        return conteudo;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Acesso
    public Task<SessaoUsuarioDto> SessaoAsync() => Get<SessaoUsuarioDto>("api/auth/sessao");
    public Task<SessaoUsuarioDto> LoginAsync(LoginDto dto) => Send<SessaoUsuarioDto>(HttpMethod.Post, "api/auth/login", dto);
    public Task<SessaoUsuarioDto> ConfigurarAcessoAsync(ConfigurarAcessoDto dto) => Send<SessaoUsuarioDto>(HttpMethod.Post, "api/auth/configurar", dto);
    public Task LogoutAsync() => Send<object>(HttpMethod.Post, "api/auth/logout", null);
    public Task TrocarSenhaAsync(TrocarSenhaDto dto) => Send<object>(HttpMethod.Post, "api/auth/senha", dto);

    // Perfil, regras e metas
    public Task<PerfilDto> PerfilAsync() => Get<PerfilDto>("api/perfil");
    public Task<PerfilDto> SalvarPerfilAsync(PerfilDto dto) => Send<PerfilDto>(HttpMethod.Put, "api/perfil", dto);
    public Task<RegrasDto> RegrasAsync() => Get<RegrasDto>("api/regras");
    public Task<RegrasDto> SalvarRegrasAsync(RegrasDto dto) => Send<RegrasDto>(HttpMethod.Put, "api/regras", dto);
    public Task<MetaDto> MetaAtualAsync() => Get<MetaDto>("api/metas/atual");
    public Task<CalculoMetaDto> SimularMetaAsync() => Get<CalculoMetaDto>("api/metas/simular");
    public Task<CalculoMetaDto> RecalcularMetaAsync() => Send<CalculoMetaDto>(HttpMethod.Post, "api/metas/recalcular", null);
    public Task<MetaDto> MetaManualAsync(MetaManualDto dto) => Send<MetaDto>(HttpMethod.Put, "api/metas/atual", dto);
    public Task<AjusteSemanalDto> AjusteSemanalAsync() => Send<AjusteSemanalDto>(HttpMethod.Post, "api/metas/ajuste-semanal", null);

    // Refeições, alimentos e favoritos
    public Task<PainelDiaDto> PainelAsync(DateOnly data) => Get<PainelDiaDto>($"api/painel/dia?data={data:yyyy-MM-dd}");
    public Task<RefeicaoDto> CriarRefeicaoAsync(NovaRefeicaoDto dto) => Send<RefeicaoDto>(HttpMethod.Post, "api/refeicoes", dto);
    public Task RemoverRefeicaoAsync(int id) => Send<object>(HttpMethod.Delete, $"api/refeicoes/{id}", null);
    public Task<List<AlimentoDto>> BuscarAlimentosAsync(string? busca, int limite = 15) =>
        Get<List<AlimentoDto>>($"api/alimentos?busca={Uri.EscapeDataString(busca ?? "")}&limite={limite}");
    public Task<AlimentoDto> CriarAlimentoAsync(NovoAlimentoDto dto) => Send<AlimentoDto>(HttpMethod.Post, "api/alimentos", dto);
    public Task<List<FavoritoDto>> FavoritosAsync() => Get<List<FavoritoDto>>("api/favoritos");
    public Task<FavoritoDto> CriarFavoritoAsync(NovoFavoritoDto dto) => Send<FavoritoDto>(HttpMethod.Post, "api/favoritos", dto);
    public Task RemoverFavoritoAsync(int id) => Send<object>(HttpMethod.Delete, $"api/favoritos/{id}", null);
    public Task<RefeicaoDto> RegistrarFavoritoAsync(int id, TipoRefeicao tipo) => Send<RefeicaoDto>(HttpMethod.Post, $"api/favoritos/{id}/registrar?tipo={tipo}", null);
    public async Task<string> ProvedorVisaoAsync() => (await Get<JsonElement>("api/visao/provedor")).GetProperty("nome").GetString() ?? "";

    public async Task<RascunhoRefeicaoDto> AnalisarFotoAsync(IBrowserFile foto, TipoRefeicao tipo, string? descricao)
    {
        // campos de texto antes da imagem: a API lê o multipart em streaming
        using var form = new MultipartFormDataContent
        {
            { new StringContent(tipo.ToString()), "tipo" },
            { new StringContent(descricao ?? ""), "descricao" }
        };
        form.Add(await PrepararFotoAsync(foto), "imagem", "foto.jpg");
        return await Ler<RascunhoRefeicaoDto>(await http.PostAsync("api/refeicoes/analisar-foto", form));
    }

    // Peso, medidas, progresso
    public Task<PontoPesoDto> RegistrarPesoAsync(NovoPesoDto dto) => Send<PontoPesoDto>(HttpMethod.Post, "api/peso", dto);
    public Task<MedidaDto> RegistrarMedidasAsync(NovasMedidasDto dto) => Send<MedidaDto>(HttpMethod.Post, "api/medidas", dto);
    public Task<ProgressoDto> ProgressoAsync(DateOnly de, DateOnly ate) => Get<ProgressoDto>($"api/progresso?de={de:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}");

    // Treino
    public Task<List<AcademiaDto>> AcademiasAsync() => Get<List<AcademiaDto>>("api/academias");
    public Task<AcademiaDto> CriarAcademiaAsync(NovaAcademiaDto dto) => Send<AcademiaDto>(HttpMethod.Post, "api/academias", dto);
    public Task<List<AparelhoDto>> AparelhosAsync() => Get<List<AparelhoDto>>("api/aparelhos");
    public Task<AparelhoDto> CriarAparelhoAsync(NovoAparelhoDto dto) => Send<AparelhoDto>(HttpMethod.Post, "api/aparelhos", dto);
    public Task<List<RotinaDto>> RotinasAsync() => Get<List<RotinaDto>>("api/rotinas");
    public Task<RotinaDto> CriarRotinaAsync(NovaRotinaDto dto) => Send<RotinaDto>(HttpMethod.Post, "api/rotinas", dto);
    public Task<List<SessaoResumoDto>> SessoesAsync() => Get<List<SessaoResumoDto>>("api/sessoes?limite=10");
    public Task<SessaoDto> IniciarSessaoAsync(NovaSessaoDto dto) => Send<SessaoDto>(HttpMethod.Post, "api/sessoes", dto);
    public Task<SessaoDto> SessaoAsync(int id) => Get<SessaoDto>($"api/sessoes/{id}");
    public Task<SerieDto> RegistrarSerieAsync(int sessaoId, NovaSerieDto dto) => Send<SerieDto>(HttpMethod.Post, $"api/sessoes/{sessaoId}/series", dto);
    public Task RemoverSerieAsync(int sessaoId, int serieId) => Send<object>(HttpMethod.Delete, $"api/sessoes/{sessaoId}/series/{serieId}", null);
    public Task<EvolucaoAparelhoDto> EvolucaoAsync(int aparelhoId) => Get<EvolucaoAparelhoDto>($"api/evolucao/{aparelhoId}");
    public Task<EvolucaoSemanaDto> EvolucaoSemanaAsync() => Get<EvolucaoSemanaDto>("api/evolucao/semana");

    public async Task<RascunhoAparelhoDto> AnalisarAparelhoAsync(IBrowserFile foto, string? nome)
    {
        using var form = new MultipartFormDataContent { { new StringContent(nome ?? ""), "nome" } };
        form.Add(await PrepararFotoAsync(foto), "imagem", "foto.jpg");
        return await Ler<RascunhoAparelhoDto>(await http.PostAsync("api/aparelhos/analisar-foto", form));
    }

    // ---------- infraestrutura ----------

    private async Task<T> Get<T>(string url) => await Ler<T>(await http.GetAsync(url));

    private async Task<T> Send<T>(HttpMethod metodo, string url, object? corpo)
    {
        using var req = new HttpRequestMessage(metodo, url);
        if (corpo != null) req.Content = JsonContent.Create(corpo, corpo.GetType(), options: Json);
        return await Ler<T>(await http.SendAsync(req));
    }

    private async Task<T> Ler<T>(HttpResponseMessage resp)
    {
        using (resp)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && !resp.RequestMessage!.RequestUri!.AbsolutePath.Contains("/api/auth/"))
            {
                sessao.Expirou();
                throw new ApiException("Sessão expirada. Entre novamente.");
            }
            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                throw new ApiException("Muitas tentativas. Aguarde um minuto e tente de novo.");
            if (resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent || typeof(T) == typeof(object)) return default!;
                return (await resp.Content.ReadFromJsonAsync<T>(Json))!;
            }
            throw new ApiException(await MensagemErro(resp));
        }
    }

    private static async Task<string> MensagemErro(HttpResponseMessage resp)
    {
        try
        {
            var p = await resp.Content.ReadFromJsonAsync<JsonElement>();
            if (p.TryGetProperty("errors", out var erros))
                return string.Join(" ", erros.EnumerateObject().SelectMany(e => e.Value.EnumerateArray().Select(v => v.GetString())));
            if (p.TryGetProperty("detail", out var d) && d.GetString() is { Length: > 0 } det) return det;
            if (p.TryGetProperty("title", out var t)) return t.GetString() ?? resp.ReasonPhrase ?? "Erro";
        }
        catch (JsonException) { }
        return $"Erro {(int)resp.StatusCode}";
    }
}

public static class Rotulos
{
    public static string Nome(this TipoRefeicao t) => t switch
    {
        TipoRefeicao.CafeDaManha => "Café da manhã",
        TipoRefeicao.Almoco => "Almoço",
        TipoRefeicao.Tarde => "Tarde",
        TipoRefeicao.Noite => "Noite",
        _ => t.ToString()
    };

    public static string Nome(this Objetivo o) => o switch
    {
        Objetivo.GanhoMassa => "Ganho de massa",
        Objetivo.Recomposicao => "Recomposição",
        Objetivo.Definicao => "Definição",
        _ => o.ToString()
    };

    public static string Nome(this NivelAtividade n) => n switch
    {
        NivelAtividade.Sedentario => "Sedentário (1,2)",
        NivelAtividade.Leve => "Leve (1,375)",
        NivelAtividade.Moderado => "Moderado (1,55)",
        NivelAtividade.Alto => "Alto (1,725)",
        NivelAtividade.MuitoAlto => "Muito alto (1,9)",
        _ => n.ToString()
    };

    public static string Nome(this TipoCarga t) => t switch
    {
        TipoCarga.PesoCorporal => "Peso corporal",
        _ => t.ToString()
    };

    public static TipoRefeicao TipoPeloHorario(DateTime agora) => agora.Hour switch
    {
        < 11 => TipoRefeicao.CafeDaManha,
        < 15 => TipoRefeicao.Almoco,
        < 19 => TipoRefeicao.Tarde,
        _ => TipoRefeicao.Noite
    };
}
