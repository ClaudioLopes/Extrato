using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Extrato.Application.Servicos;

/// <summary>Seção 10: aparelhos, rotinas, sessões, séries e evolução.</summary>
public class TreinoService(
    IAppDbContext db,
    IUsuarioAtual usuario,
    IRelogio relogio,
    IVisionProvider vision,
    IProcessadorImagem processador,
    ILogger<TreinoService> logger)
{
    // ---------- Academias e aparelhos ----------

    public async Task<IReadOnlyList<AcademiaDto>> AcademiasAsync(CancellationToken ct) =>
        await db.Academias.OrderBy(a => a.Nome).Select(a => new AcademiaDto(a.Id, a.Nome)).ToListAsync(ct);

    public async Task<AcademiaDto> CriarAcademiaAsync(NovaAcademiaDto dto, CancellationToken ct)
    {
        var a = new Academia { Nome = dto.Nome.Trim() };
        db.Academias.Add(a);
        await db.SaveChangesAsync(ct);
        return new AcademiaDto(a.Id, a.Nome);
    }

    public async Task<IReadOnlyList<AparelhoDto>> AparelhosAsync(int? academiaId, CancellationToken ct) =>
        (await db.Aparelhos.Include(a => a.Academia)
            .Where(a => academiaId == null || a.AcademiaId == academiaId)
            .OrderBy(a => a.GrupoMuscular).ThenBy(a => a.Nome).ToListAsync(ct))
        .Select(a => a.ParaDto()).ToList();

    public async Task<AparelhoDto> CriarAparelhoAsync(NovoAparelhoDto dto, CancellationToken ct)
    {
        var academia = await db.Academias.FindAsync([dto.AcademiaId], ct) ?? throw new ValidacaoException("Academia inexistente.");
        var a = new Aparelho
        {
            AcademiaId = academia.Id, Academia = academia, Nome = dto.Nome.Trim(), Exercicio = dto.Exercicio.Trim(),
            GrupoMuscular = dto.GrupoMuscular.Trim(), TipoCarga = dto.TipoCarga, IncrementoKg = dto.IncrementoKg
        };
        db.Aparelhos.Add(a);
        await db.SaveChangesAsync(ct);
        return a.ParaDto();
    }

    /// <summary>Seção 10.1: foto só no cadastro; com pessoas no enquadramento o resultado é descartado.</summary>
    public async Task<RascunhoAparelhoDto> AnalisarFotoAparelhoAsync(Stream imagem, string? nome, CancellationToken ct)
    {
        await using var preparada = await processador.PrepararAsync(imagem, ct);
        try
        {
            var r = await vision.AnalisarAparelhoAsync(preparada, nome, ct);
            if (r.HaPessoas)
                return new RascunhoAparelhoDto(null, null, null,
                    ["Há pessoas no enquadramento: resultado descartado. Fotografe só o aparelho."], true, false, null);
            TipoCarga? tipo = Enum.TryParse<TipoCarga>(r.TipoCarga?.Replace(" ", ""), true, out var t) ? t : null;
            return new RascunhoAparelhoDto(r.Exercicio, r.GrupoMuscular, tipo, r.Alertas, false, false, null);
        }
        catch (VisionIndisponivelException ex)
        {
            logger.LogWarning("IA de visão indisponível para aparelho: {Motivo}", ex.Message);
            return new RascunhoAparelhoDto(nome, null, null, [], false, true, ex.Message);
        }
    }

    // ---------- Rotinas ----------

    public async Task<IReadOnlyList<RotinaDto>> RotinasAsync(CancellationToken ct) =>
        (await db.Rotinas.Include(r => r.Itens).ThenInclude(i => i.Aparelho).OrderBy(r => r.Nome).ToListAsync(ct))
        .Select(ParaDto).ToList();

    public async Task<RotinaDto> CriarRotinaAsync(NovaRotinaDto dto, CancellationToken ct)
    {
        if (dto.Itens.Any(i => i.RepsMin > i.RepsMax)) throw new ValidacaoException("Reps mínimas maiores que as máximas.");
        var ids = dto.Itens.Select(i => i.AparelhoId).Distinct().ToList();
        var aparelhos = await db.Aparelhos.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        if (aparelhos.Count != ids.Count) throw new ValidacaoException("Há aparelhos inexistentes na rotina.");
        var r = new Rotina
        {
            Nome = dto.Nome.Trim(),
            Itens = dto.Itens.Select((i, idx) => new RotinaItem
            {
                AparelhoId = i.AparelhoId, Aparelho = aparelhos[i.AparelhoId], Ordem = idx + 1,
                SeriesAlvo = i.SeriesAlvo, RepsMin = i.RepsMin, RepsMax = i.RepsMax
            }).ToList()
        };
        db.Rotinas.Add(r);
        await db.SaveChangesAsync(ct);
        return ParaDto(r);
    }

    // ---------- Sessões ----------

    public async Task<IReadOnlyList<SessaoResumoDto>> SessoesRecentesAsync(int limite, CancellationToken ct) =>
        (await db.Sessoes.Include(s => s.Rotina).Include(s => s.Series)
            .OrderByDescending(s => s.DataHora).Take(Math.Clamp(limite, 1, 100)).ToListAsync(ct))
        .Select(s => new SessaoResumoDto(s.Id, s.DataHora, s.Rotina?.Nome, s.Series.Count,
            MetricasTreino.Volume(s.Series.Select(x => new SerieRegistrada(x.CargaKg, x.Reps)))))
        .ToList();

    public async Task<SessaoDto> IniciarSessaoAsync(NovaSessaoDto dto, CancellationToken ct)
    {
        if (dto.RotinaId is { } rid && !await db.Rotinas.AnyAsync(r => r.Id == rid, ct))
            throw new ValidacaoException("Rotina inexistente.");
        var s = new SessaoTreino { DataHora = relogio.Agora, RotinaId = dto.RotinaId, Observacao = dto.Observacao };
        db.Sessoes.Add(s);
        await db.SaveChangesAsync(ct);
        return await SessaoAsync(s.Id, ct);
    }

    public async Task<SerieDto> RegistrarSerieAsync(int sessaoId, NovaSerieDto dto, CancellationToken ct)
    {
        var sessao = await db.Sessoes.Include(s => s.Series).FirstOrDefaultAsync(s => s.Id == sessaoId, ct)
            ?? throw new NaoEncontradoException("Sessão não encontrada.");
        if (!await db.Aparelhos.AnyAsync(a => a.Id == dto.AparelhoId, ct)) throw new ValidacaoException("Aparelho inexistente.");
        var serie = new Serie
        {
            SessaoId = sessao.Id, AparelhoId = dto.AparelhoId, CargaKg = dto.CargaKg, Reps = dto.Reps,
            RepsNaReserva = dto.RepsNaReserva,
            Ordem = sessao.Series.Count(x => x.AparelhoId == dto.AparelhoId) + 1
        };
        db.Series.Add(serie);
        await db.SaveChangesAsync(ct);
        return serie.ParaDto();
    }

    public async Task RemoverSerieAsync(int sessaoId, int serieId, CancellationToken ct)
    {
        var s = await db.Series.FirstOrDefaultAsync(x => x.Id == serieId && x.SessaoId == sessaoId, ct)
            ?? throw new NaoEncontradoException("Série não encontrada.");
        db.Series.Remove(s);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sessão com séries pré-preenchidas pela última sessão e comparação (resumo de fim de sessão).</summary>
    public async Task<SessaoDto> SessaoAsync(int id, CancellationToken ct)
    {
        var s = await db.Sessoes.Include(x => x.Rotina!).ThenInclude(r => r.Itens).ThenInclude(i => i.Aparelho!).ThenInclude(a => a.Academia)
            .Include(x => x.Series).ThenInclude(x => x.Aparelho!).ThenInclude(a => a.Academia)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NaoEncontradoException("Sessão não encontrada.");

        var itensRotina = s.Rotina?.Itens.OrderBy(i => i.Ordem).ToList() ?? [];
        var aparelhos = itensRotina.Select(i => i.Aparelho!)
            .Concat(s.Series.Select(x => x.Aparelho!))
            .DistinctBy(a => a.Id).ToList();

        var ids = aparelhos.Select(a => a.Id).ToList();
        var anteriores = await db.Series.Include(x => x.Sessao)
            .Where(x => ids.Contains(x.AparelhoId) && x.SessaoId != s.Id && x.Sessao!.DataHora < s.DataHora)
            .ToListAsync(ct);

        var exercicios = aparelhos.Select(a =>
        {
            var item = itensRotina.FirstOrDefault(i => i.AparelhoId == a.Id);
            var hoje = s.Series.Where(x => x.AparelhoId == a.Id).OrderBy(x => x.Ordem).ToList();
            var ultimaSessaoId = anteriores.Where(x => x.AparelhoId == a.Id)
                .OrderByDescending(x => x.Sessao!.DataHora).Select(x => (int?)x.SessaoId).FirstOrDefault();
            var ultima = anteriores.Where(x => x.AparelhoId == a.Id && x.SessaoId == ultimaSessaoId).OrderBy(x => x.Ordem).ToList();
            var regHoje = hoje.Select(x => new SerieRegistrada(x.CargaKg, x.Reps)).ToList();
            var regAnt = ultima.Select(x => new SerieRegistrada(x.CargaKg, x.Reps)).ToList();
            return new ExercicioSessaoDto(a.ParaDto(), item?.SeriesAlvo, item?.RepsMin, item?.RepsMax,
                hoje.Select(x => x.ParaDto()).ToList(), ultima.Select(x => x.ParaDto()).ToList(),
                regHoje.Count > 0 ? regHoje.Max(x => MetricasTreino.Epley(x.CargaKg, x.Reps)) : null,
                regAnt.Count > 0 ? regAnt.Max(x => MetricasTreino.Epley(x.CargaKg, x.Reps)) : null,
                MetricasTreino.Volume(regHoje), MetricasTreino.Volume(regAnt));
        }).ToList();

        return new SessaoDto(s.Id, s.DataHora, s.RotinaId, s.Rotina?.Nome, s.Observacao, exercicios);
    }

    // ---------- Evolução ----------

    public async Task<EvolucaoAparelhoDto> EvolucaoAsync(int aparelhoId, CancellationToken ct)
    {
        var aparelho = await db.Aparelhos.Include(a => a.Academia).FirstOrDefaultAsync(a => a.Id == aparelhoId, ct)
            ?? throw new NaoEncontradoException("Aparelho não encontrado.");
        var sessoes = await SessoesDoAparelhoAsync(aparelhoId, null, ct);

        var pontos = sessoes.Select(s => new PontoEvolucaoDto(s.DataHora, s.Melhor1Rm, s.Volume,
            s.Series.Max(x => x.CargaKg), s.Series.Max(x => x.Reps))).ToList();

        var repsMax = await db.Rotinas.SelectMany(r => r.Itens).Where(i => i.AparelhoId == aparelhoId)
            .Select(i => (int?)i.RepsMax).FirstOrDefaultAsync(ct) ?? 12;
        var sugestao = sessoes.Count > 0 ? MetricasTreino.SugestaoCarga(UltimaCargaDeTrabalho(sessoes[^1].Series), repsMax, aparelho.IncrementoKg) : null;

        return new EvolucaoAparelhoDto(aparelho.ParaDto(), pontos, MetricasTreino.RecordePessoal(sessoes),
            MetricasTreino.Estagnado(sessoes), sugestao);
    }

    public async Task<EvolucaoSemanaDto> SemanaAsync(CancellationToken ct)
    {
        var ate = relogio.Hoje;
        var de = ate.AddDays(-6);
        var ini = de.ToDateTime(TimeOnly.MinValue);
        var series = await db.Series.Include(x => x.Aparelho).Include(x => x.Sessao)
            .Where(x => x.Sessao!.DataHora >= ini).ToListAsync(ct);

        var grupos = series.GroupBy(x => x.Aparelho!.GrupoMuscular)
            .Select(g => new VolumeGrupoDto(g.Key, g.Sum(x => x.CargaKg * x.Reps), g.Count()))
            .OrderByDescending(g => g.Volume).ToList();

        var alertas = new List<string>();
        foreach (var aparelho in await db.Aparelhos.ToListAsync(ct))
        {
            var sessoes = await SessoesDoAparelhoAsync(aparelho.Id, null, ct);
            if (MetricasTreino.Estagnado(sessoes))
                alertas.Add($"{aparelho.Nome}: {MetricasTreino.SessoesParaEstagnacao} sessões sem subir 1RM estimado nem repetições.");
        }
        return new EvolucaoSemanaDto(de, ate, grupos, alertas);
    }

    /// <summary>Força (média dos melhores 1RM por aparelho) e cintura nas últimas 2 semanas vs. as 2 anteriores.</summary>
    public async Task<string?> LeituraParaAjusteAsync(DateOnly referencia, CancellationToken ct)
    {
        var corte = referencia.AddDays(-13).ToDateTime(TimeOnly.MinValue);
        var inicio = referencia.AddDays(-27).ToDateTime(TimeOnly.MinValue);
        var series = await db.Series.Include(x => x.Sessao)
            .Where(x => x.Sessao!.DataHora >= inicio).ToListAsync(ct);

        var variacoes = series.GroupBy(x => x.AparelhoId).Select(g =>
        {
            var antes = g.Where(x => x.Sessao!.DataHora < corte).Select(x => MetricasTreino.Epley(x.CargaKg, x.Reps)).DefaultIfEmpty(0).Max();
            var depois = g.Where(x => x.Sessao!.DataHora >= corte).Select(x => MetricasTreino.Epley(x.CargaKg, x.Reps)).DefaultIfEmpty(0).Max();
            return antes > 0 && depois > 0 ? (decimal?)((depois - antes) / antes * 100m) : null;
        }).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        decimal? forca = variacoes.Count == 0 ? null : Math.Round(variacoes.Average(), 1);

        var medidas = await db.Medidas.Where(m => m.UsuarioId == usuario.UsuarioId && m.Data <= referencia)
            .OrderByDescending(m => m.Data).Take(3).ToListAsync(ct);
        decimal? cintura = medidas.Count >= 2 ? medidas[0].CinturaCm - medidas[^1].CinturaCm : null;

        return LeituraTreino.Interpretar(forca, cintura);
    }

    private async Task<List<SessaoAparelho>> SessoesDoAparelhoAsync(int aparelhoId, DateTime? desde, CancellationToken ct)
    {
        var series = await db.Series.Include(x => x.Sessao)
            .Where(x => x.AparelhoId == aparelhoId && (desde == null || x.Sessao!.DataHora >= desde))
            .ToListAsync(ct);
        return series.GroupBy(x => x.SessaoId)
            .Select(g => new SessaoAparelho(g.First().Sessao!.DataHora, g.Select(x => new SerieRegistrada(x.CargaKg, x.Reps)).ToList()))
            .OrderBy(s => s.DataHora).ToList();
    }

    /// <summary>Séries na carga mais alta da sessão (aquecimentos ficam de fora da dupla progressão).</summary>
    private static List<SerieRegistrada> UltimaCargaDeTrabalho(IReadOnlyList<SerieRegistrada> series)
    {
        var max = series.Max(s => s.CargaKg);
        return series.Where(s => s.CargaKg == max).ToList();
    }

    private static RotinaDto ParaDto(Rotina r) =>
        new(r.Id, r.Nome, r.Itens.OrderBy(i => i.Ordem).Select(i => new RotinaItemDto(
            i.AparelhoId, i.Aparelho?.Nome ?? "", i.Aparelho?.Exercicio ?? "", i.Ordem, i.SeriesAlvo, i.RepsMin, i.RepsMax)).ToList());
}
