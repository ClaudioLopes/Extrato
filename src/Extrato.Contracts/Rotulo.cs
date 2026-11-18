namespace Extrato.Contracts;

/// <summary>Rótulos trazem valores por porção (ex.: 10 g de pré-treino); a tabela do app guarda por 100 g.</summary>
public static class Rotulo
{
    public static decimal Por100g(decimal valorNaPorcao, decimal porcaoG) =>
        porcaoG <= 0 ? valorNaPorcao : Math.Round(valorNaPorcao * 100m / porcaoG, 1, MidpointRounding.AwayFromZero);

    /// <summary>Monta o cadastro (por 100 g) a partir dos números do rótulo na porção indicada.</summary>
    public static NovoAlimentoDto ParaCadastro(string nome, decimal porcaoG, decimal kcal, decimal proteinaG, decimal carboG,
        decimal gorduraG, decimal? fibraG, IEnumerable<string> sinonimos) => new()
    {
        Nome = nome.Trim(),
        Kcal100g = Math.Round(Por100g(kcal, porcaoG), 0, MidpointRounding.AwayFromZero),
        Proteina100g = Por100g(proteinaG, porcaoG),
        Carbo100g = Por100g(carboG, porcaoG),
        Gordura100g = Por100g(gorduraG, porcaoG),
        Fibra100g = fibraG is { } f ? Por100g(f, porcaoG) : null,
        Sinonimos = sinonimos.ToList()
    };
}
