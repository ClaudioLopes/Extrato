using Extrato.Domain.Entidades;

namespace Extrato.Domain.Regras;

public record PerfilCalculo(Sexo Sexo, decimal PesoKg, decimal AlturaCm, int Idade, NivelAtividade Nivel, Objetivo Objetivo);

public record ResultadoMeta(
    decimal Tmb,
    decimal FatorAtividade,
    decimal Get,
    int AjusteKcal,
    int Kcal,
    decimal ProteinaG,
    decimal CarboG,
    decimal GorduraG,
    IReadOnlyList<string> Racional)
{
    public Macros ComoMacros() => new(Kcal, ProteinaG, CarboG, GorduraG);
}

/// <summary>Seção 6.1 a 6.3 e travas da 6.6.</summary>
public static class CalculadoraMetas
{
    public const decimal GorduraMinimaFracaoKcal = 0.20m;

    /// <summary>Mifflin-St Jeor.</summary>
    public static decimal Tmb(Sexo sexo, decimal pesoKg, decimal alturaCm, int idade) =>
        10m * pesoKg + 6.25m * alturaCm - 5m * idade + (sexo == Sexo.Masculino ? 5m : -161m);

    public static decimal FatorAtividade(NivelAtividade nivel) => nivel switch
    {
        NivelAtividade.Sedentario => 1.2m,
        NivelAtividade.Leve => 1.375m,
        NivelAtividade.Moderado => 1.55m,
        NivelAtividade.Alto => 1.725m,
        NivelAtividade.MuitoAlto => 1.9m,
        _ => throw new ArgumentOutOfRangeException(nameof(nivel))
    };

    /// <summary>Ponto central das faixas da tabela 6.2.</summary>
    public static int AjustePadrao(Objetivo objetivo) => objetivo switch
    {
        Objetivo.GanhoMassa => 300,
        Objetivo.Recomposicao => 75,
        Objetivo.Definicao => -400,
        _ => throw new ArgumentOutOfRangeException(nameof(objetivo))
    };

    public static (int Min, int Max) FaixaAjuste(Objetivo objetivo) => objetivo switch
    {
        Objetivo.GanhoMassa => (250, 400),
        Objetivo.Recomposicao => (0, 150),
        Objetivo.Definicao => (-500, -300),
        _ => throw new ArgumentOutOfRangeException(nameof(objetivo))
    };

    public static ResultadoMeta Calcular(PerfilCalculo p, ConfiguracaoRegras regras)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(p.PesoKg);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(p.AlturaCm);

        var racional = new List<string>();
        var travas = regras.Travas;

        var tmb = Tmb(p.Sexo, p.PesoKg, p.AlturaCm, p.Idade);
        racional.Add($"TMB (Mifflin-St Jeor): 10×{p.PesoKg} + 6,25×{p.AlturaCm} − 5×{p.Idade} {(p.Sexo == Sexo.Masculino ? "+ 5" : "− 161")} = {tmb:0} kcal");

        var fator = regras.FatorAtividade ?? FatorAtividade(p.Nivel);
        var get = tmb * fator;
        racional.Add($"GET = TMB × {fator} ({(regras.FatorAtividade.HasValue ? "fator manual" : p.Nivel.ToString())}) = {get:0} kcal. O ajuste semanal calibra esse valor.");

        var ajuste = regras.AjusteKcal ?? AjustePadrao(p.Objetivo);
        var ajusteTravado = Math.Clamp(ajuste, -travas.DeficitMaxKcal, travas.SuperavitMaxKcal);
        if (ajusteTravado != ajuste)
            racional.Add($"Ajuste de {ajuste:+0;−0} kcal limitado pelas travas para {ajusteTravado:+0;−0} kcal.");
        racional.Add($"Objetivo {p.Objetivo}: {ajusteTravado:+0;−0;0} kcal sobre o GET.");

        var kcal = (int)Math.Round(get + ajusteTravado, MidpointRounding.AwayFromZero);
        var tmbInteira = (int)Math.Ceiling(tmb);
        if (kcal < tmbInteira)
        {
            racional.Add($"Meta de {kcal} kcal abaixo da TMB; elevada para {tmbInteira} kcal (trava de calorias mínimas).");
            kcal = tmbInteira;
        }

        var protGKg = Math.Min(regras.ProteinaGPorKg, travas.ProteinaMaxGPorKg);
        if (protGKg != regras.ProteinaGPorKg)
            racional.Add($"Proteína limitada a {travas.ProteinaMaxGPorKg} g/kg (trava).");
        var proteina = R(protGKg * p.PesoKg);
        racional.Add($"Proteína: {protGKg} g/kg × {p.PesoKg} kg = {proteina} g ({proteina * 4:0} kcal).");

        var gordura = R(regras.GorduraGPorKg * p.PesoKg);
        var gorduraMin = R(kcal * GorduraMinimaFracaoKcal / Macros.KcalPorGGordura);
        if (gordura < gorduraMin)
        {
            racional.Add($"Gordura de {gordura} g ficaria abaixo de 20% das calorias; elevada para {gorduraMin} g.");
            gordura = gorduraMin;
        }
        else
        {
            racional.Add($"Gordura: {regras.GorduraGPorKg} g/kg × {p.PesoKg} kg = {gordura} g ({gordura * 9:0} kcal).");
        }

        var carbo = R((kcal - proteina * Macros.KcalPorGProteina - gordura * Macros.KcalPorGGordura) / Macros.KcalPorGCarbo);
        if (carbo < 0)
        {
            racional.Add("Proteína e gordura já ultrapassam a meta calórica; carboidrato zerado. Revise os coeficientes.");
            carbo = 0;
        }
        else
        {
            racional.Add($"Carboidrato: o que sobra = ({kcal} − {proteina * 4:0} − {gordura * 9:0}) / 4 = {carbo} g.");
        }

        return new ResultadoMeta(R(tmb), fator, R(get), ajusteTravado, kcal, proteina, carbo, gordura, racional);
    }

    private static decimal R(decimal v) => Math.Round(v, 1, MidpointRounding.AwayFromZero);
}
