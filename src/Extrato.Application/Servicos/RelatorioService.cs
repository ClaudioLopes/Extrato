using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Regras;
using Microsoft.EntityFrameworkCore;

namespace Extrato.Application.Servicos;

/// <summary>Fase 2: relatórios semanal (7 dias) e mensal (30 dias).</summary>
public class RelatorioService(IAppDbContext db, IUsuarioAtual usuario, IRelogio relogio, MetaService metas)
{
    /// <summary>Dia "na meta" quando as calorias ficam a até 10% da meta do dia.</summary>
    public const decimal ToleranciaKcal = 0.10m;
    /// <summary>Proteína "atingida" a partir de 90% da meta.</summary>
    public const decimal ProteinaAtingida = 0.90m;

    public async Task<RelatorioDto> GerarAsync(string periodo, DateOnly? ate, CancellationToken ct)
    {
        var p = periodo.Trim().ToLowerInvariant() switch
        {
            "semana" or "semanal" => "semana",
            "mes" or "mês" or "mensal" => "mes",
            _ => throw new ValidacaoException("Período inválido: use 'semana' ou 'mes'.")
        };
        var fim = ate ?? relogio.Hoje;
        var inicio = fim.AddDays(p == "semana" ? -6 : -29);
        var uid = usuario.UsuarioId;

        var ini = inicio.ToDateTime(TimeOnly.MinValue);
        var depois = fim.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var refeicoes = await db.Refeicoes.Where(r => r.UsuarioId == uid && r.DataHora >= ini && r.DataHora < depois)
            .Include(r => r.Itens).ToListAsync(ct);
        var consumoPorDia = refeicoes.GroupBy(r => DateOnly.FromDateTime(r.DataHora))
            .ToDictionary(g => g.Key, g => Macros.Somar(g.Select(r => r.Total())));

        var pesos = (await db.Pesos.Where(x => x.UsuarioId == uid && x.Data >= inicio.AddDays(-6) && x.Data <= fim)
                .Select(x => new { x.Data, x.PesoKg }).ToListAsync(ct))
            .Select(x => (x.Data, x.PesoKg)).ToList();

        var dias = new List<DiaRelatorioDto>();
        for (var d = inicio; d <= fim; d = d.AddDays(1))
        {
            var meta = await metas.MetaDoDiaAsync(d, ct);
            var consumido = consumoPorDia.GetValueOrDefault(d, Macros.Zero);
            var peso = pesos.Where(x => x.Data == d).Select(x => (decimal?)x.PesoKg).FirstOrDefault();
            dias.Add(new DiaRelatorioDto(d, MacrosDto.De(meta?.Macros ?? Macros.Zero), MacrosDto.De(consumido),
                consumoPorDia.ContainsKey(d), meta?.DiaDeTreino ?? false, peso));
        }

        var registrados = dias.Where(d => d.Registrado && d.Meta.Kcal > 0).ToList();
        MacrosDto Media(Func<DiaRelatorioDto, MacrosDto> sel) => registrados.Count == 0 ? MacrosDto.Zero : new MacrosDto(
            Math.Round(registrados.Average(d => sel(d).Kcal)), Math.Round(registrados.Average(d => sel(d).ProteinaG), 1),
            Math.Round(registrados.Average(d => sel(d).CarboG), 1), Math.Round(registrados.Average(d => sel(d).GorduraG), 1));

        var naMeta = registrados.Count(d => Math.Abs(d.Consumido.Kcal - d.Meta.Kcal) <= d.Meta.Kcal * ToleranciaKcal);
        var proteina = registrados.Count(d => d.Consumido.ProteinaG >= d.Meta.ProteinaG * ProteinaAtingida);

        var pesoIni = MediaMovel.Em(pesos, inicio);
        var pesoFim = MediaMovel.Em(pesos, fim);

        var medidas = await db.Medidas.Where(m => m.UsuarioId == uid && m.Data <= fim).OrderBy(m => m.Data)
            .Select(m => new { m.Data, m.CinturaCm }).ToListAsync(ct);
        var cinturaAntes = medidas.LastOrDefault(m => m.Data <= inicio) ?? medidas.FirstOrDefault(m => m.Data >= inicio);
        var cinturaDepois = medidas.LastOrDefault();
        decimal? cintura = cinturaAntes != null && cinturaDepois != null && cinturaDepois.Data > cinturaAntes.Data
            ? cinturaDepois.CinturaCm - cinturaAntes.CinturaCm : null;

        var (sessoes, volume, recordes) = await TreinoAsync(ini, depois, ct);

        var ajustes = await db.AjustesMeta.Where(a => a.UsuarioId == uid && a.Data >= inicio && a.Data <= fim)
            .OrderBy(a => a.Data).ToListAsync(ct);

        var destaques = Destaques(dias.Count, registrados.Count, naMeta, proteina, pesoIni, pesoFim, cintura, sessoes, recordes.Count);

        return new RelatorioDto(p, inicio, fim, dias, registrados.Count, Media(d => d.Consumido), Media(d => d.Meta),
            naMeta, proteina, pesoIni, pesoFim, cintura, sessoes, volume, recordes,
            ajustes.Select(a => a.ParaDto()).ToList(), destaques);
    }

    private async Task<(int Sessoes, decimal Volume, List<string> Recordes)> TreinoAsync(DateTime ini, DateTime depois, CancellationToken ct)
    {
        var series = await db.Series.Include(s => s.Sessao).Include(s => s.Aparelho)
            .Where(s => s.Sessao!.DataHora < depois).ToListAsync(ct);
        var noPeriodo = series.Where(s => s.Sessao!.DataHora >= ini).ToList();
        var sessoes = noPeriodo.Select(s => s.SessaoId).Distinct().Count();
        var volume = MetricasTreino.Volume(noPeriodo.Select(s => new SerieRegistrada(s.CargaKg, s.Reps)));

        var recordes = new List<string>();
        foreach (var g in noPeriodo.GroupBy(s => s.AparelhoId))
        {
            var melhorAgora = g.Max(s => MetricasTreino.Epley(s.CargaKg, s.Reps));
            var antes = series.Where(s => s.AparelhoId == g.Key && s.Sessao!.DataHora < ini)
                .Select(s => MetricasTreino.Epley(s.CargaKg, s.Reps)).DefaultIfEmpty(0).Max();
            if (antes > 0 && melhorAgora > antes)
                recordes.Add($"{g.First().Aparelho?.Nome}: recorde de {melhorAgora:0.#} kg (1RM est.), antes {antes:0.#} kg");
        }
        return (sessoes, volume, recordes);
    }

    internal static List<string> Destaques(int totalDias, int registrados, int naMeta, int proteina,
        decimal? pesoIni, decimal? pesoFim, decimal? cintura, int sessoes, int recordes)
    {
        var d = new List<string>();
        if (registrados == 0)
        {
            d.Add("Nenhum dia com refeições registradas no período.");
            return d;
        }
        d.Add($"Registro em {registrados} de {totalDias} dias.");
        d.Add($"Calorias dentro de ±10% da meta em {naMeta} de {registrados} dias registrados ({100m * naMeta / registrados:0}%).");
        d.Add($"Proteína atingida (≥90%) em {proteina} de {registrados} dias.");
        if (pesoIni is { } a && pesoFim is { } b)
            d.Add($"Peso (média de 7 dias): {a:0.0} → {b:0.0} kg ({b - a:+0.0;−0.0;0.0} kg).");
        if (cintura is { } c)
            d.Add($"Cintura: {c:+0.0;−0.0;0.0} cm no período.");
        if (sessoes > 0)
            d.Add($"{sessoes} sessão(ões) de treino{(recordes > 0 ? $", {recordes} recorde(s) pessoal(is)" : "")}.");
        if (registrados < totalDias * 5 / 7)
            d.Add("Poucos dias registrados: o ajuste semanal precisa de pelo menos 5 por semana.");
        return d;
    }
}
