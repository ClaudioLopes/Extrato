using Extrato.Domain.Entidades;

namespace Extrato.Domain.Regras;

public record MetaDoDia(Macros Macros, bool DiaDeTreino, int DeltaKcal, string? Explicacao);

/// <summary>
/// Fase 2: metas diferentes para dia de treino e de descanso. O dia de treino ganha carboidrato
/// extra; os dias de descanso perdem o mesmo total, de modo que a média da semana não muda
/// (o ajuste semanal continua valendo sobre a meta base).
/// </summary>
public static class CicloTreino
{
    public static bool EhDiaDeTreino(DateOnly data, IReadOnlyCollection<DayOfWeek> diasPlanejados, bool houveSessao) =>
        houveSessao || diasPlanejados.Contains(data.DayOfWeek);

    /// <summary>Redução nos dias de descanso que compensa o extra dos dias de treino na semana.</summary>
    public static int ReducaoDescanso(int extraTreino, int diasTreinoSemana)
    {
        if (diasTreinoSemana <= 0 || diasTreinoSemana >= 7 || extraTreino <= 0) return 0;
        return (int)Math.Round((decimal)extraTreino * diasTreinoSemana / (7 - diasTreinoSemana), MidpointRounding.AwayFromZero);
    }

    public static MetaDoDia Aplicar(Macros metaBase, ConfiguracaoRegras regras, bool diaDeTreino, decimal tmb)
    {
        if (!regras.CicloTreinoAtivo || regras.KcalExtraDiaTreino <= 0)
            return new MetaDoDia(metaBase, diaDeTreino, 0, null);

        var dias = regras.DiasTreino.Count;
        var delta = diaDeTreino ? regras.KcalExtraDiaTreino : -ReducaoDescanso(regras.KcalExtraDiaTreino, dias);
        if (!diaDeTreino && dias is 0 or 7) delta = 0;

        // trava: o dia de descanso nunca desce abaixo da TMB, e o carboidrato não fica negativo
        var minimo = (int)Math.Ceiling(tmb);
        if (metaBase.Kcal + delta < minimo) delta = (int)Math.Min(0, minimo - metaBase.Kcal);
        var carbo = Math.Max(0, metaBase.CarboG + delta / Macros.KcalPorGCarbo);
        delta = (int)Math.Round((carbo - metaBase.CarboG) * Macros.KcalPorGCarbo);

        var ajustada = metaBase with { Kcal = metaBase.Kcal + delta, CarboG = Math.Round(carbo, 1) };
        var texto = delta == 0 ? null
            : diaDeTreino
                ? $"Dia de treino: +{delta} kcal em carboidrato."
                : $"Dia de descanso: {delta} kcal em carboidrato (compensa os {dias} dias de treino da semana).";
        return new MetaDoDia(ajustada, diaDeTreino, delta, texto);
    }
}
