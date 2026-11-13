namespace Extrato.Domain;

public readonly record struct Macros(decimal Kcal, decimal ProteinaG, decimal CarboG, decimal GorduraG)
{
    public const decimal KcalPorGProteina = 4m;
    public const decimal KcalPorGCarbo = 4m;
    public const decimal KcalPorGGordura = 9m;

    public static Macros Zero => new(0, 0, 0, 0);

    /// <summary>gramas / 100 × valor por 100 g.</summary>
    public static Macros DePorcao(decimal gramas, decimal kcal100, decimal prot100, decimal carbo100, decimal gord100)
    {
        var f = gramas / 100m;
        return new(R(kcal100 * f), R(prot100 * f), R(carbo100 * f), R(gord100 * f));
    }

    public static Macros Somar(IEnumerable<Macros> itens) =>
        itens.Aggregate(Zero, (a, b) => a + b);

    public static Macros operator +(Macros a, Macros b) =>
        new(a.Kcal + b.Kcal, a.ProteinaG + b.ProteinaG, a.CarboG + b.CarboG, a.GorduraG + b.GorduraG);

    public static Macros operator -(Macros a, Macros b) =>
        new(a.Kcal - b.Kcal, a.ProteinaG - b.ProteinaG, a.CarboG - b.CarboG, a.GorduraG - b.GorduraG);

    public Macros Vezes(decimal f) => new(R(Kcal * f), R(ProteinaG * f), R(CarboG * f), R(GorduraG * f));

    private static decimal R(decimal v) => Math.Round(v, 1, MidpointRounding.AwayFromZero);
}
