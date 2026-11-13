namespace Extrato.Domain.Regras;

public record SerieRegistrada(decimal CargaKg, int Reps);

public record SessaoAparelho(DateTime DataHora, IReadOnlyList<SerieRegistrada> Series)
{
    public decimal Melhor1Rm => Series.Count == 0 ? 0 : Series.Max(s => MetricasTreino.Epley(s.CargaKg, s.Reps));
    public decimal Volume => MetricasTreino.Volume(Series);
}

/// <summary>Seção 10.3.</summary>
public static class MetricasTreino
{
    public const int SessoesParaEstagnacao = 3;

    /// <summary>1RM estimado (Epley): carga × (1 + reps / 30). Confiável até ~12 reps.</summary>
    public static decimal Epley(decimal cargaKg, int reps)
    {
        if (reps <= 0 || cargaKg <= 0) return 0;
        if (reps == 1) return cargaKg;
        return Math.Round(cargaKg * (1 + reps / 30m), 1);
    }

    public static decimal Volume(IEnumerable<SerieRegistrada> series) => series.Sum(s => s.CargaKg * s.Reps);

    public static decimal RecordePessoal(IEnumerable<SessaoAparelho> sessoes) =>
        sessoes.Select(s => s.Melhor1Rm).DefaultIfEmpty(0).Max();

    /// <summary>
    /// 3 sessões seguidas sem subir o 1RM estimado nem as repetições na mesma carga,
    /// em relação à sessão anterior a elas.
    /// </summary>
    public static bool Estagnado(IReadOnlyList<SessaoAparelho> sessoesOrdenadas)
    {
        if (sessoesOrdenadas.Count < SessoesParaEstagnacao + 1) return false;
        var ultimas = sessoesOrdenadas.TakeLast(SessoesParaEstagnacao + 1).ToList();
        var baseSessao = ultimas[0];
        var melhorRepsPorCarga = MelhoresReps(baseSessao.Series);
        var melhor1Rm = baseSessao.Melhor1Rm;

        foreach (var s in ultimas.Skip(1))
        {
            if (s.Melhor1Rm > melhor1Rm) return false;
            foreach (var serie in s.Series)
                if (melhorRepsPorCarga.TryGetValue(serie.CargaKg, out var reps) && serie.Reps > reps)
                    return false;
        }
        return true;
    }

    /// <summary>
    /// Dupla progressão: bateu o topo da faixa em todas as séries → sugerir +2,5% a 5%
    /// ou o próximo incremento do aparelho. Retorna null quando não é hora de subir.
    /// </summary>
    public static decimal? SugestaoCarga(IReadOnlyList<SerieRegistrada> ultimaSessao, int repsMax, decimal? incrementoKg)
    {
        if (ultimaSessao.Count == 0 || repsMax <= 0) return null;
        if (ultimaSessao.Any(s => s.Reps < repsMax)) return null;
        var carga = ultimaSessao.Max(s => s.CargaKg);
        if (incrementoKg is > 0) return carga + incrementoKg.Value;
        var minimo = carga * 1.025m;
        // arredonda para o próximo múltiplo de 0,5 kg, sem passar de +5%
        var sugerida = Math.Ceiling(minimo * 2) / 2;
        return Math.Min(sugerida, Math.Max(carga * 1.05m, carga + 0.5m));
    }

    private static Dictionary<decimal, int> MelhoresReps(IEnumerable<SerieRegistrada> series) =>
        series.GroupBy(s => s.CargaKg).ToDictionary(g => g.Key, g => g.Max(s => s.Reps));
}

/// <summary>Seção 10.4: leitura combinada de força e cintura que entra no motivo do ajuste semanal.</summary>
public static class LeituraTreino
{
    public static string? Interpretar(decimal? variacaoForcaPercent, decimal? variacaoCinturaCm)
    {
        if (variacaoForcaPercent is null || variacaoCinturaCm is null) return null;
        var forcaSubindo = variacaoForcaPercent > 1m;
        var cinturaSubindo = variacaoCinturaCm > 0.5m;
        return (forcaSubindo, cinturaSubindo) switch
        {
            (true, false) => $"Força {variacaoForcaPercent:+0.0}% e cintura estável/caindo ({variacaoCinturaCm:+0.0;−0.0;0} cm): estratégia funcionando.",
            (false, true) => $"Força parada ({variacaoForcaPercent:+0.0;−0.0;0}%) e cintura subindo ({variacaoCinturaCm:+0.0} cm): superávit provavelmente alto demais.",
            (true, true) => $"Força {variacaoForcaPercent:+0.0}% com cintura subindo ({variacaoCinturaCm:+0.0} cm): ganho com algum acúmulo de gordura.",
            _ => $"Força ({variacaoForcaPercent:+0.0;−0.0;0}%) e cintura ({variacaoCinturaCm:+0.0;−0.0;0} cm) estáveis."
        };
    }
}
