using Extrato.Domain.Entidades;

namespace Extrato.Domain.Regras;

public record EntradaAjusteSemanal(
    DateOnly Referencia,
    Objetivo Objetivo,
    int KcalMetaAtual,
    decimal Tmb,
    IReadOnlyList<(DateOnly Data, decimal PesoKg)> Pesos,
    IReadOnlyDictionary<DateOnly, decimal> IngestaoKcalPorDia,
    DateOnly? UltimoAjuste,
    Travas Travas,
    string? LeituraTreino = null,
    /// <summary>Calorias de atividades creditadas na meta de cada dia (descontadas da ingestão ao estimar o GET base).</summary>
    IReadOnlyDictionary<DateOnly, int>? AtividadeKcalPorDia = null);

public record ResultadoAjusteSemanal(
    bool Ajustar,
    int KcalAnterior,
    int KcalNova,
    int? GetEstimado,
    decimal? RitmoPercentSemana,
    string Motivo);

/// <summary>Seção 6.5: a meta só muda pela tendência semanal do peso.</summary>
public static class AjusteSemanal
{
    public const decimal KcalPorKg = 7700m;
    public const int DiasMinimosRegistrados = 5;
    public const int PassoKcal = 125;

    public static (decimal Min, decimal Max) FaixaRitmo(Objetivo objetivo) => objetivo switch
    {
        Objetivo.GanhoMassa => (0.25m, 0.5m),
        Objetivo.Recomposicao => (-0.25m, 0.25m),
        Objetivo.Definicao => (-1.0m, -0.5m),
        _ => throw new ArgumentOutOfRangeException(nameof(objetivo))
    };

    /// <summary>GET_real ≈ ingestão média diária − (Δpeso_semana_kg × 7.700 / 7).</summary>
    public static decimal GetAdaptativo(decimal ingestaoMediaDiaria, decimal deltaPesoSemanaKg) =>
        ingestaoMediaDiaria - deltaPesoSemanaKg * KcalPorKg / 7m;

    public static ResultadoAjusteSemanal Avaliar(EntradaAjusteSemanal e)
    {
        var atual = e.KcalMetaAtual;
        ResultadoAjusteSemanal SemAjuste(string motivo, int? get = null, decimal? ritmo = null) =>
            new(false, atual, atual, get, ritmo, motivo);

        var ref0 = e.Referencia;
        var inicioSemana = ref0.AddDays(-6);
        var diasSemana = e.IngestaoKcalPorDia.Where(kv => kv.Key >= inicioSemana && kv.Key <= ref0 && kv.Value > 0).ToList();
        if (diasSemana.Count < DiasMinimosRegistrados)
            return SemAjuste($"Só {diasSemana.Count} dia(s) com refeições registradas na semana; são necessários {DiasMinimosRegistrados}.");

        var mmAtual = MediaMovel.Em(e.Pesos, ref0);
        var mmSemanaPassada = MediaMovel.Em(e.Pesos, ref0.AddDays(-7));
        var mmDuasSemanas = MediaMovel.Em(e.Pesos, ref0.AddDays(-14));
        if (mmAtual is null || mmSemanaPassada is null)
            return SemAjuste("Faltam registros de peso para comparar a média móvel de 7 dias com a semana anterior.");

        var delta = mmAtual.Value - mmSemanaPassada.Value;
        var ingestaoMedia = diasSemana.Average(kv => kv.Value);
        // a meta base não inclui as atividades registradas (elas somam só no dia): o GET comparado com ela
        // também não pode incluir, senão o gasto delas entraria duas vezes
        var atividadeMedia = diasSemana.Average(kv => (decimal)(e.AtividadeKcalPorDia?.GetValueOrDefault(kv.Key) ?? 0));
        var getReal = (int)Math.Round(GetAdaptativo(ingestaoMedia - atividadeMedia, delta));
        var ritmo = Math.Round(delta / mmSemanaPassada.Value * 100m, 2);
        var faixa = FaixaRitmo(e.Objetivo);
        var desc = $"Média móvel {mmSemanaPassada:0.0} → {mmAtual:0.0} kg ({ritmo:+0.00;−0.00}%/semana; faixa {faixa.Min:+0.00;−0.00}% a {faixa.Max:+0.00;−0.00}%). Ingestão média {ingestaoMedia:0} kcal" +
                   (atividadeMedia > 0 ? $" (das quais {atividadeMedia:0} kcal/dia de atividades registradas, descontadas)" : "") +
                   $", GET base estimado {getReal} kcal.";
        if (!string.IsNullOrWhiteSpace(e.LeituraTreino)) desc += " " + e.LeituraTreino;

        var direcaoAtual = Direcao(ritmo, faixa);
        if (direcaoAtual == 0)
            return SemAjuste("Ritmo dentro da faixa do objetivo. " + desc, getReal, ritmo);

        if (mmDuasSemanas is null)
            return SemAjuste("Fora da faixa nesta semana, mas sem dados da semana anterior para confirmar 2 semanas seguidas. " + desc, getReal, ritmo);

        var ritmoAnterior = (mmSemanaPassada.Value - mmDuasSemanas.Value) / mmDuasSemanas.Value * 100m;
        if (Direcao(ritmoAnterior, faixa) != direcaoAtual)
            return SemAjuste("Fora da faixa só nesta semana; aguardando a segunda semana para ajustar. " + desc, getReal, ritmo);

        if (e.UltimoAjuste is { } ultimo && ref0.DayNumber - ultimo.DayNumber < 7)
            return SemAjuste($"Já houve ajuste em {ultimo:dd/MM}; no máximo 1 ajuste por semana. " + desc, getReal, ritmo);

        // abaixo da faixa → sobe calorias; acima → desce
        var passo = Math.Min(PassoKcal, e.Travas.VariacaoMaxKcalSemana) * (direcaoAtual < 0 ? 1 : -1);
        var nova = atual + passo;
        nova = Math.Clamp(nova, getReal - e.Travas.DeficitMaxKcal, getReal + e.Travas.SuperavitMaxKcal);
        nova = Math.Clamp(nova, atual - e.Travas.VariacaoMaxKcalSemana, atual + e.Travas.VariacaoMaxKcalSemana);
        var minimo = (int)Math.Ceiling(e.Tmb);
        if (nova < minimo) nova = minimo;

        if (nova == atual)
            return SemAjuste("Fora da faixa por 2 semanas, mas as travas impedem mudar a meta. " + desc, getReal, ritmo);

        var sentido = direcaoAtual < 0 ? "abaixo" : "acima";
        return new ResultadoAjusteSemanal(true, atual, nova, getReal, ritmo,
            $"Ritmo {sentido} da faixa por 2 semanas seguidas: meta {atual} → {nova} kcal. " + desc);
    }

    /// <summary>-1 abaixo da faixa, 0 dentro, +1 acima.</summary>
    private static int Direcao(decimal ritmo, (decimal Min, decimal Max) faixa) =>
        ritmo < faixa.Min ? -1 : ritmo > faixa.Max ? 1 : 0;
}

public static class MediaMovel
{
    /// <summary>Média dos pesos registrados nos 7 dias que terminam em <paramref name="data"/>.</summary>
    public static decimal? Em(IEnumerable<(DateOnly Data, decimal PesoKg)> pesos, DateOnly data, int janelaDias = 7)
    {
        var inicio = data.AddDays(-(janelaDias - 1));
        var janela = pesos.Where(p => p.Data >= inicio && p.Data <= data).Select(p => p.PesoKg).ToList();
        return janela.Count == 0 ? null : Math.Round(janela.Average(), 2);
    }
}
