using Extrato.Domain.Entidades;

namespace Extrato.Domain.Regras;

/// <summary>
/// Atividades fora da musculação na meta: o dia ganha uma parte das calorias ativas da pulseira
/// (padrão 75%, porque pulseiras costumam superestimar), em carboidrato. O ajuste semanal desconta
/// essas mesmas calorias antes de estimar o gasto base, para não contá-las duas vezes.
/// </summary>
public static class GastoAtividades
{
    /// <summary>
    /// MET médio por atividade (Compendium of Physical Activities, intensidade moderada/recreativa).
    /// Só usado quando não há número da pulseira.
    /// </summary>
    public static decimal Met(TipoAtividade tipo) => tipo switch
    {
        TipoAtividade.Natacao => 7.0m,
        TipoAtividade.Volei => 4.0m,
        TipoAtividade.Corrida => 9.0m,
        TipoAtividade.Caminhada => 3.5m,
        TipoAtividade.Ciclismo => 7.0m,
        TipoAtividade.Futebol => 7.0m,
        _ => 5.0m
    };

    /// <summary>Calorias ativas estimadas: (MET − 1) × peso × horas (o 1 MET de repouso já está no GET).</summary>
    public static int EstimarKcalAtivas(TipoAtividade tipo, int duracaoMin, decimal pesoKg) =>
        (int)Math.Round((Met(tipo) - 1m) * pesoKg * duracaoMin / 60m, MidpointRounding.AwayFromZero);

    /// <summary>Ativas a partir das totais da pulseira: tira o metabolismo de repouso do período (TMB proporcional).</summary>
    public static int AtivasDasTotais(int kcalTotais, int duracaoMin, decimal tmb) =>
        Math.Max(0, kcalTotais - (int)Math.Round(tmb * duracaoMin / 1440m, MidpointRounding.AwayFromZero));

    public static string Nome(this TipoAtividade tipo) => tipo switch
    {
        TipoAtividade.Natacao => "Natação",
        TipoAtividade.Volei => "Vôlei",
        TipoAtividade.Corrida => "Corrida",
        TipoAtividade.Caminhada => "Caminhada",
        TipoAtividade.Ciclismo => "Ciclismo",
        TipoAtividade.Futebol => "Futebol",
        _ => "Atividade"
    };

    /// <summary>"Natação", "Natação e vôlei", "Natação, vôlei e corrida".</summary>
    public static string Descrever(IEnumerable<TipoAtividade> tipos)
    {
        var nomes = tipos.Distinct().Select((t, i) => i == 0 ? t.Nome() : t.Nome().ToLowerInvariant()).ToList();
        return nomes.Count switch
        {
            0 => "Atividades",
            1 => nomes[0],
            _ => string.Join(", ", nomes[..^1]) + " e " + nomes[^1]
        };
    }

    /// <summary>Calorias creditadas na meta: a soma das ativas vezes o percentual configurado.</summary>
    public static int Credito(IEnumerable<int> kcalAtivas, ConfiguracaoRegras regras)
    {
        if (!regras.AtividadesNaMeta) return 0;
        var pct = Math.Clamp(regras.PercentualKcalAtividade, 0, 100);
        return (int)Math.Round(kcalAtivas.Sum() * pct / 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>Soma o crédito das atividades à meta do dia (já com o ciclo treino × descanso aplicado).</summary>
    public static MetaDoDia Somar(MetaDoDia meta, int credito, string descricao, int percentual)
    {
        if (credito <= 0) return meta;
        var macros = meta.Macros with
        {
            Kcal = meta.Macros.Kcal + credito,
            CarboG = Math.Round(meta.Macros.CarboG + credito / Macros.KcalPorGCarbo, 1)
        };
        var texto = $"{descricao}: +{credito} kcal em carboidrato ({percentual}% das calorias ativas).";
        return meta with
        {
            Macros = macros,
            DeltaKcal = meta.DeltaKcal + credito,
            Explicacao = meta.Explicacao is null ? texto : meta.Explicacao + " " + texto
        };
    }
}
