using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Extrato.Contracts;
using Extrato.Domain;
using Microsoft.AspNetCore.Components.Forms;

namespace Extrato.Web.Servicos;

public class ApiException(string mensagem, int? status = null) : Exception(mensagem)
{
    public int? Status => status;
}

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
public class ApiClient(HttpClient http, SessaoEstado sessao, ArmazenamentoLocal local, RedeEstado rede)
{
    private bool _sincronizando;
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
    /// <summary>Sem conexão, usa a última sessão conhecida para abrir o app no modo offline.</summary>
    public Task<SessaoUsuarioDto> SessaoAsync() => GetSalvo<SessaoUsuarioDto>("api/auth/sessao", "sessao");
    public async Task<SessaoUsuarioDto> LoginAsync(LoginDto dto) => await SalvarSessao(await Send<SessaoUsuarioDto>(HttpMethod.Post, "api/auth/login", dto));
    public async Task<SessaoUsuarioDto> ConfigurarAcessoAsync(ConfigurarAcessoDto dto) => await SalvarSessao(await Send<SessaoUsuarioDto>(HttpMethod.Post, "api/auth/configurar", dto));

    private async Task<SessaoUsuarioDto> SalvarSessao(SessaoUsuarioDto s)
    {
        await local.GravarAsync("sessao", new Salvo<SessaoUsuarioDto>(s, DateTime.Now));
        return s;
    }

    /// <summary>Ao sair, apaga todos os dados salvos no aparelho (inclusive a fila).</summary>
    public Task LimparDadosLocaisAsync() => local.LimparTudoAsync();
    public Task LogoutAsync() => Send<object>(HttpMethod.Post, "api/auth/logout", null);
    public Task TrocarSenhaAsync(TrocarSenhaDto dto) => Send<object>(HttpMethod.Post, "api/auth/senha", dto);

    // Perfil, regras e metas
    public Task<PerfilDto> PerfilAsync() => GetSalvo<PerfilDto>("api/perfil", "perfil");
    public Task<PerfilDto> SalvarPerfilAsync(PerfilDto dto) => Send<PerfilDto>(HttpMethod.Put, "api/perfil", dto);
    public Task<RegrasDto> RegrasAsync() => GetSalvo<RegrasDto>("api/regras", "regras");
    public Task<RegrasDto> SalvarRegrasAsync(RegrasDto dto) => Send<RegrasDto>(HttpMethod.Put, "api/regras", dto);
    public Task<MetaDto> MetaAtualAsync() => GetSalvo<MetaDto>("api/metas/atual", "meta");
    public Task<CalculoMetaDto> SimularMetaAsync() => Get<CalculoMetaDto>("api/metas/simular");
    public Task<CalculoMetaDto> RecalcularMetaAsync() => Send<CalculoMetaDto>(HttpMethod.Post, "api/metas/recalcular", null);
    public Task<MetaDto> MetaManualAsync(MetaManualDto dto) => Send<MetaDto>(HttpMethod.Put, "api/metas/atual", dto);
    public Task<AjusteSemanalDto> AjusteSemanalAsync() => Send<AjusteSemanalDto>(HttpMethod.Post, "api/metas/ajuste-semanal", null);

    // Refeições, alimentos e favoritos
    /// <summary>Online: do servidor (e salva). Offline: último painel salvo do dia + fila, recalculado no aparelho.</summary>
    public async Task<PainelDiaDto> PainelAsync(DateOnly data)
    {
        var chave = $"painel:{data:yyyy-MM-dd}";
        try
        {
            var p = await Get<PainelDiaDto>($"api/painel/dia?data={data:yyyy-MM-dd}");
            await local.GravarAsync(chave, new Salvo<PainelDiaDto>(p, DateTime.Now));
            return p;
        }
        catch (SemConexaoException)
        {
            var salvo = await local.LerAsync<Salvo<PainelDiaDto>>(chave)
                ?? throw new ApiException("Sem conexão e sem dados salvos deste dia. Abra o painel com internet ao menos uma vez.");
            var regras = await local.LerAsync<Salvo<RegrasDto>>("regras");
            var previas = (await LerFila()).Where(f => f.Previa != null).Select(f => f.Previa!);
            return PainelLocal.Combinar(salvo.Valor, previas, regras?.Valor, DateTime.Now, salvo.SalvoEm);
        }
    }

    /// <summary>Offline (ou se a rede cair no envio), a refeição vai para a fila e é enviada depois.</summary>
    public async Task<RefeicaoDto> CriarRefeicaoAsync(NovaRefeicaoDto dto)
    {
        dto.IdCliente ??= Guid.NewGuid();
        dto.DataHora ??= DateTime.Now; // hora do lançamento, não a do envio
        if (rede.Online)
        {
            try { return await Send<RefeicaoDto>(HttpMethod.Post, "api/refeicoes", dto); }
            catch (SemConexaoException) { }
        }
        var previa = PainelLocal.RefeicaoLocal(dto, await CatalogoPorIdAsync(), DateTime.Now);
        await Enfileirar(new Pendente(dto.IdCliente.Value, TipoPendente.Refeicao, DateTime.Now, dto, null, previa));
        return previa;
    }
    public Task RemoverRefeicaoAsync(int id) => Send<object>(HttpMethod.Delete, $"api/refeicoes/{id}", null);
    /// <summary>Offline, busca no catálogo salvo com a mesma pontuação do servidor.</summary>
    public async Task<List<AlimentoDto>> BuscarAlimentosAsync(string? busca, int limite = 15)
    {
        if (rede.Online)
        {
            try { return await Get<List<AlimentoDto>>($"api/alimentos?busca={Uri.EscapeDataString(busca ?? "")}&limite={limite}"); }
            catch (SemConexaoException) { }
        }
        var catalogo = await local.LerAsync<CatalogoAlimentosDto>("catalogo");
        return catalogo is null ? [] : PainelLocal.Buscar(catalogo.Itens, busca ?? "", limite);
    }

    public async Task<AlimentoDto> CriarAlimentoAsync(NovoAlimentoDto dto)
    {
        var a = await Send<AlimentoDto>(HttpMethod.Post, "api/alimentos", dto);
        await AtualizarCatalogoAsync();
        return a;
    }

    public Task<List<FavoritoDto>> FavoritosAsync() => GetSalvo<List<FavoritoDto>>("api/favoritos", "favoritos");
    public Task<FavoritoDto> CriarFavoritoAsync(NovoFavoritoDto dto) => Send<FavoritoDto>(HttpMethod.Post, "api/favoritos", dto);
    public Task RemoverFavoritoAsync(int id) => Send<object>(HttpMethod.Delete, $"api/favoritos/{id}", null);
    public async Task<RefeicaoDto> RegistrarFavoritoAsync(int id, TipoRefeicao tipo)
    {
        // vira uma refeição comum com IdCliente: funciona igual online e offline
        var favoritos = await FavoritosAsync();
        var f = favoritos.FirstOrDefault(x => x.Id == id) ?? throw new ApiException("Favorito não encontrado.");
        return await CriarRefeicaoAsync(new NovaRefeicaoDto
        {
            Tipo = tipo, Origem = OrigemRefeicao.Favorito, Descricao = f.Nome,
            Itens = f.Itens.Select(i => new NovoItemRefeicaoDto { AlimentoId = i.AlimentoId, Gramas = i.Gramas }).ToList()
        });
    }
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

    // Fase 2
    public Task<CardapioDto> CardapioAsync(TipoRefeicao tipo, DateOnly data) => Get<CardapioDto>($"api/cardapio?tipo={tipo}&data={data:yyyy-MM-dd}");
    public Task<RelatorioDto> RelatorioAsync(string periodo, DateOnly ate) => Get<RelatorioDto>($"api/relatorios?periodo={periodo}&ate={ate:yyyy-MM-dd}");
    public Task<AvaliacaoCorporalDto> FotoCorpoSituacaoAsync() => Get<AvaliacaoCorporalDto>("api/medidas/foto-permitida");

    public async Task<AvaliacaoCorporalDto> AnalisarCorpoAsync(IBrowserFile foto, string? descricao)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("true"), "consentimento" },
            { new StringContent(descricao ?? ""), "descricao" }
        };
        form.Add(await PrepararFotoAsync(foto), "imagem", "foto.jpg");
        return await Ler<AvaliacaoCorporalDto>(await http.PostAsync("api/medidas/analisar-foto", form));
    }

    // Peso, medidas, progresso
    public async Task<PontoPesoDto> RegistrarPesoAsync(NovoPesoDto dto)
    {
        dto.Data ??= DateOnly.FromDateTime(DateTime.Now);
        if (rede.Online)
        {
            try { return await Send<PontoPesoDto>(HttpMethod.Post, "api/peso", dto); }
            catch (SemConexaoException) { }
        }
        await Enfileirar(new Pendente(Guid.NewGuid(), TipoPendente.Peso, DateTime.Now, null, dto, null));
        return new PontoPesoDto(dto.Data.Value, dto.PesoKg, null);
    }
    public Task<MedidaDto> RegistrarMedidasAsync(NovasMedidasDto dto) => Send<MedidaDto>(HttpMethod.Post, "api/medidas", dto);
    public Task<ProgressoDto> ProgressoAsync(DateOnly de, DateOnly ate) => GetSalvo<ProgressoDto>($"api/progresso?de={de:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}", "progresso");

    // Treino (telas salvas no aparelho; sessão e séries funcionam offline pela fila)
    public Task<List<AcademiaDto>> AcademiasAsync() => GetSalvo<List<AcademiaDto>>("api/academias", "academias");
    public Task<AcademiaDto> CriarAcademiaAsync(NovaAcademiaDto dto) => Send<AcademiaDto>(HttpMethod.Post, "api/academias", dto);
    public Task<List<AparelhoDto>> AparelhosAsync() => GetSalvo<List<AparelhoDto>>("api/aparelhos", "aparelhos");
    public Task<AparelhoDto> CriarAparelhoAsync(NovoAparelhoDto dto) => Send<AparelhoDto>(HttpMethod.Post, "api/aparelhos", dto);
    public Task<List<RotinaDto>> RotinasAsync() => GetSalvo<List<RotinaDto>>("api/rotinas", "rotinas");
    public Task<RotinaDto> CriarRotinaAsync(NovaRotinaDto dto) => Send<RotinaDto>(HttpMethod.Post, "api/rotinas", dto);
    public Task<List<SessaoResumoDto>> SessoesAsync() => GetSalvo<List<SessaoResumoDto>>("api/sessoes?limite=10", "sessoes");
    public Task<Dictionary<int, List<SerieDto>>> UltimasSeriesAsync() =>
        GetSalvo<Dictionary<int, List<SerieDto>>>("api/treino/ultimas-series", "treino:ultimas");
    public Task<EvolucaoAparelhoDto> EvolucaoAsync(int aparelhoId) => Get<EvolucaoAparelhoDto>($"api/evolucao/{aparelhoId}");
    public Task<EvolucaoSemanaDto> EvolucaoSemanaAsync() => GetSalvo<EvolucaoSemanaDto>("api/evolucao/semana", "treino:semana");

    /// <summary>
    /// Online: cria no servidor. Offline (ou se a rede cair no envio): entra na fila e a sessão é montada
    /// no aparelho com Id 0; a página a abre pela rota local, com o IdCliente.
    /// </summary>
    public async Task<SessaoDto> IniciarSessaoAsync(NovaSessaoDto dto)
    {
        dto.IdCliente ??= Guid.NewGuid();
        if (rede.Online)
        {
            try { return await Send<SessaoDto>(HttpMethod.Post, "api/sessoes", dto); }
            catch (SemConexaoException) { }
        }
        dto.DataHora ??= DateTime.Now; // hora do início, não a do envio
        await Enfileirar(new Pendente(dto.IdCliente.Value, TipoPendente.Sessao, DateTime.Now, null, null, null) { Sessao = dto });
        return await SessaoLocalAsync(dto.IdCliente.Value);
    }

    /// <summary>Sessão do servidor (salva no aparelho) mais as séries dela que ainda estão na fila.</summary>
    public async Task<SessaoDto> SessaoAsync(int id)
    {
        var sessao = await GetSalvo<SessaoDto>($"api/sessoes/{id}", $"treino:sessao:{id}");
        var fila = await LerFila();
        return await CombinarPendentesAsync(sessao, fila.Where(f => f.SessaoId == id));
    }

    /// <summary>
    /// Sessão iniciada offline. Ainda na fila: montada no aparelho (Id 0). Já enviada: a do servidor (Id &gt; 0),
    /// e a página passa para a rota normal.
    /// </summary>
    public async Task<SessaoDto> SessaoLocalAsync(Guid idCliente)
    {
        var fila = await LerFila();
        var pendente = fila.FirstOrDefault(f => f.Tipo == TipoPendente.Sessao && f.Id == idCliente);
        if (pendente?.Sessao is null)
            return await Get<SessaoDto>($"api/sessoes/cliente/{idCliente}");

        var rotinas = await LerSalvoAsync<List<RotinaDto>>("rotinas") ?? [];
        var dto = pendente.Sessao;
        var sessao = TreinoLocal.NovaSessao(dto.DataHora ?? pendente.CriadoEm, rotinas.FirstOrDefault(r => r.Id == dto.RotinaId),
            await LerSalvoAsync<List<AparelhoDto>>("aparelhos") ?? [], await UltimasSalvasAsync(), dto.Observacao);
        return await CombinarPendentesAsync(sessao, fila.Where(f => f.SessaoIdCliente == idCliente));
    }

    private async Task<SessaoDto> CombinarPendentesAsync(SessaoDto sessao, IEnumerable<Pendente> itens)
    {
        // série que o servidor já tem (enviada, mas a fila ainda não foi regravada) não aparece duas vezes
        var jaNoServidor = sessao.Exercicios.SelectMany(e => e.SeriesHoje).Select(s => s.IdCliente).OfType<Guid>().ToHashSet();
        var series = itens.Where(f => f.Serie is not null && !jaNoServidor.Contains(f.Serie.IdCliente)).Select(f => f.Serie!).ToList();
        if (series.Count == 0) return sessao;
        return TreinoLocal.Combinar(sessao, series, await LerSalvoAsync<List<AparelhoDto>>("aparelhos") ?? [], await UltimasSalvasAsync());
    }

    private async Task<Dictionary<int, List<SerieDto>>> UltimasSalvasAsync() =>
        await LerSalvoAsync<Dictionary<int, List<SerieDto>>>("treino:ultimas") ?? [];

    /// <summary>
    /// Série de uma sessão do servidor (sessaoId &gt; 0) ou de uma iniciada offline (sessaoIdCliente).
    /// Sem conexão, ou numa sessão que ainda está na fila, a série entra na fila.
    /// </summary>
    public async Task RegistrarSerieAsync(int sessaoId, Guid? sessaoIdCliente, NovaSerieDto entrada)
    {
        var dto = new NovaSerieDto
        {
            AparelhoId = entrada.AparelhoId, CargaKg = entrada.CargaKg, Reps = entrada.Reps,
            RepsNaReserva = entrada.RepsNaReserva, IdCliente = Guid.NewGuid()
        };
        if (sessaoId > 0 && rede.Online)
        {
            try { await Send<SerieDto>(HttpMethod.Post, $"api/sessoes/{sessaoId}/series", dto); return; }
            catch (SemConexaoException) { }
        }
        if (sessaoId <= 0 && sessaoIdCliente is null) throw new ApiException("Sessão não encontrada.");
        var serie = new SeriePendente(dto.IdCliente.Value, dto.AparelhoId, dto.CargaKg, dto.Reps, dto.RepsNaReserva, DateTime.Now);
        await Enfileirar(new Pendente(serie.IdCliente, TipoPendente.Serie, serie.CriadaEm, null, null, null)
        {
            Serie = serie,
            SessaoId = sessaoId > 0 ? sessaoId : null,
            SessaoIdCliente = sessaoId > 0 ? null : sessaoIdCliente
        });
        // sessão iniciada offline com a rede de volta: envia sessão e série juntas, na ordem
        if (sessaoId <= 0 && rede.Online) await SincronizarAsync();
    }

    /// <summary>Série ainda na fila sai só do aparelho; a do servidor precisa de conexão.</summary>
    public async Task RemoverSerieAsync(int sessaoId, SerieDto serie)
    {
        if (serie.Pendente && serie.IdCliente is { } id)
        {
            var fila = await LerFila();
            fila.RemoveAll(f => f.Tipo == TipoPendente.Serie && f.Id == id);
            await local.GravarAsync("fila", fila);
            rede.DefinirPendentes(fila.Count);
            return;
        }
        await Send<object>(HttpMethod.Delete, $"api/sessoes/{sessaoId}/series/{serie.Id}", null);
    }

    /// <summary>Sessões iniciadas offline que ainda não foram enviadas.</summary>
    public async Task<List<SessaoPendente>> SessoesPendentesAsync()
    {
        var fila = await LerFila();
        return fila.Where(f => f.Tipo == TipoPendente.Sessao && f.Sessao is not null)
            .Select(f => new SessaoPendente(f.Id, f.Sessao!.DataHora ?? f.CriadoEm, f.Sessao.RotinaId,
                fila.Count(s => s.SessaoIdCliente == f.Id)))
            .OrderByDescending(s => s.DataHora).ToList();
    }

    public async Task<RascunhoAparelhoDto> AnalisarAparelhoAsync(IBrowserFile foto, string? nome)
    {
        using var form = new MultipartFormDataContent { { new StringContent(nome ?? ""), "nome" } };
        form.Add(await PrepararFotoAsync(foto), "imagem", "foto.jpg");
        return await Ler<RascunhoAparelhoDto>(await http.PostAsync("api/aparelhos/analisar-foto", form));
    }

    // ---------- infraestrutura ----------

    private Task<T> Get<T>(string url) => Enviar<T>(new HttpRequestMessage(HttpMethod.Get, url));

    private Task<T> Send<T>(HttpMethod metodo, string url, object? corpo)
    {
        var req = new HttpRequestMessage(metodo, url);
        if (corpo != null) req.Content = JsonContent.Create(corpo, corpo.GetType(), options: Json);
        return Enviar<T>(req);
    }

    /// <summary>Falha de rede vira SemConexaoException e marca o app como offline; resposta marca online.</summary>
    private async Task<T> Enviar<T>(HttpRequestMessage req)
    {
        HttpResponseMessage resp;
        using (req)
        {
            try { resp = await http.SendAsync(req); }
            catch (HttpRequestException) { rede.DefinirOnline(false); throw new SemConexaoException(); }
            catch (TaskCanceledException) { rede.DefinirOnline(false); throw new SemConexaoException(); }
        }
        rede.DefinirOnline(true);
        return await Ler<T>(resp);
    }

    /// <summary>GET que salva a resposta no aparelho e, sem conexão, devolve a última versão salva.</summary>
    private async Task<T> GetSalvo<T>(string url, string chave)
    {
        try
        {
            var v = await Get<T>(url);
            await local.GravarAsync(chave, new Salvo<T>(v, DateTime.Now));
            return v;
        }
        catch (SemConexaoException)
        {
            var salvo = await local.LerAsync<Salvo<T>>(chave);
            if (salvo is not null) return salvo.Valor;
            throw new ApiException("Sem conexão e sem dados salvos desta tela.");
        }
    }

    private async Task<T?> LerSalvoAsync<T>(string chave) where T : class => (await local.LerAsync<Salvo<T>>(chave))?.Valor;

    // ---------- offline: catálogo, fila e sincronização ----------

    /// <summary>Baixa o catálogo de alimentos só quando mudou (ETag).</summary>
    public async Task AtualizarCatalogoAsync()
    {
        var atual = await local.LerAsync<CatalogoAlimentosDto>("catalogo");
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/alimentos/catalogo");
        if (atual is not null) req.Headers.TryAddWithoutValidation("If-None-Match", $"\"{atual.Versao}\"");
        HttpResponseMessage resp;
        try { resp = await http.SendAsync(req); }
        catch (HttpRequestException) { rede.DefinirOnline(false); return; }
        using (resp)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.NotModified || !resp.IsSuccessStatusCode) return;
            var novo = await resp.Content.ReadFromJsonAsync<CatalogoAlimentosDto>(Json);
            if (novo is not null) await local.GravarAsync("catalogo", novo);
        }
    }

    /// <summary>
    /// Com conexão, deixa salvo o que o modo offline precisa: catálogo, regras, favoritos, perfil e, para
    /// iniciar um treino sem internet, aparelhos, rotinas e as últimas séries de cada aparelho.
    /// </summary>
    public async Task PrepararOfflineAsync()
    {
        await AtualizarCatalogoAsync();
        foreach (var carregar in new Func<Task>[] { RegrasAsync, FavoritosAsync, PerfilAsync, AparelhosAsync, RotinasAsync, UltimasSeriesAsync })
        {
            try { await carregar(); } catch (ApiException) { /* tenta de novo na próxima abertura */ }
        }
    }

    private async Task<Dictionary<int, AlimentoDto>> CatalogoPorIdAsync()
    {
        var catalogo = await local.LerAsync<CatalogoAlimentosDto>("catalogo");
        var mapa = catalogo?.Itens.ToDictionary(i => i.Alimento.Id, i => i.Alimento) ?? [];
        // favoritos salvos completam o que faltar (macros por 100 g a partir da porção)
        var favoritos = await local.LerAsync<Salvo<List<FavoritoDto>>>("favoritos");
        foreach (var item in favoritos?.Valor.SelectMany(f => f.Itens) ?? [])
            if (!mapa.ContainsKey(item.AlimentoId) && item.Gramas > 0)
            {
                var f = 100m / item.Gramas;
                mapa[item.AlimentoId] = new AlimentoDto(item.AlimentoId, item.Alimento, FonteAlimento.Taco,
                    item.Macros.Kcal * f, item.Macros.ProteinaG * f, item.Macros.CarboG * f, item.Macros.GorduraG * f, null);
            }
        return mapa;
    }

    public async Task<List<Pendente>> LerFila() => await local.LerAsync<List<Pendente>>("fila") ?? [];

    private async Task Enfileirar(Pendente p)
    {
        var fila = await LerFila();
        if (fila.All(f => f.Id != p.Id)) fila.Add(p);
        if (!await local.GravarAsync("fila", fila))
            throw new ApiException("Sem conexão e não foi possível salvar no aparelho (armazenamento cheio ou bloqueado).");
        rede.DefinirPendentes(fila.Count);
    }

    /// <summary>
    /// Envia a fila em ordem. Para na primeira falha de rede ou sessão expirada (tenta de novo depois);
    /// erro 5xx também espera; erro de validação (4xx) descarta o item e avisa o usuário.
    /// </summary>
    public async Task<int> SincronizarAsync()
    {
        if (_sincronizando) return 0;
        _sincronizando = true;
        var fila = await LerFila();
        var enviados = 0;
        try
        {
            if (fila.Count == 0) return 0;
            rede.DefinirPendentes(fila.Count, sincronizando: true);
            foreach (var p in fila.OrderBy(f => f.CriadoEm).ToList())
            {
                try
                {
                    await EnviarPendente(p);
                    fila.Remove(p);
                    enviados++;
                }
                catch (SemConexaoException) { break; }
                catch (SessaoExpiradaException) { break; }
                catch (ApiException ex) when (ex.Status is null or >= 500 or 429) { break; }
                catch (ApiException ex)
                {
                    fila.Remove(p);
                    rede.Rejeitar($"{Descrever(p)} não foi aceito pelo servidor: {ex.Message}");
                }
                await local.GravarAsync("fila", fila);
            }
        }
        finally
        {
            _sincronizando = false;
            rede.DefinirPendentes(fila.Count);
        }
        if (enviados > 0) rede.AvisarSincronizou();
        return enviados;
    }

    /// <summary>A fila vai em ordem: a sessão iniciada offline chega antes das séries dela.</summary>
    private Task EnviarPendente(Pendente p) => p.Tipo switch
    {
        TipoPendente.Refeicao => Send<RefeicaoDto>(HttpMethod.Post, "api/refeicoes", p.Refeicao),
        TipoPendente.Peso => Send<PontoPesoDto>(HttpMethod.Post, "api/peso", p.Peso),
        TipoPendente.Sessao => Send<SessaoDto>(HttpMethod.Post, "api/sessoes", p.Sessao),
        TipoPendente.Serie => Send<SerieDto>(HttpMethod.Post,
            p.SessaoId is { } id ? $"api/sessoes/{id}/series" : $"api/sessoes/cliente/{p.SessaoIdCliente}/series",
            new NovaSerieDto
            {
                AparelhoId = p.Serie!.AparelhoId, CargaKg = p.Serie.CargaKg, Reps = p.Serie.Reps,
                RepsNaReserva = p.Serie.RepsNaReserva, IdCliente = p.Serie.IdCliente
            }),
        _ => throw new ApiException("Tipo de lançamento desconhecido.", 400)
    };

    private static string Descrever(Pendente p) => p.Tipo switch
    {
        TipoPendente.Peso => $"Peso de {p.Peso!.Data:dd/MM}",
        TipoPendente.Sessao => $"Sessão de treino de {p.CriadoEm:dd/MM HH:mm}",
        TipoPendente.Serie => $"Série de {p.Serie!.CargaKg:0.#} kg × {p.Serie.Reps} ({p.CriadoEm:dd/MM HH:mm})",
        _ => $"{p.Refeicao!.Tipo.Nome()} de {p.CriadoEm:dd/MM HH:mm}"
    };

    private async Task<T> Ler<T>(HttpResponseMessage resp)
    {
        using (resp)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && !resp.RequestMessage!.RequestUri!.AbsolutePath.Contains("/api/auth/"))
            {
                sessao.Expirou();
                throw new SessaoExpiradaException();
            }
            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                throw new ApiException("Muitas tentativas. Aguarde um minuto e tente de novo.", 429);
            if (resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent || typeof(T) == typeof(object)) return default!;
                return (await resp.Content.ReadFromJsonAsync<T>(Json))!;
            }
            throw new ApiException(await MensagemErro(resp), (int)resp.StatusCode);
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
