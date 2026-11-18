using Extrato.Application;

namespace Extrato.Infrastructure.Visao;

/// <summary>
/// Leitura da tabela nutricional de um rótulo, comum ao Claude e ao Gemini. Campos que não aparecem
/// vêm como 0 ou "" (o schema não usa null).
/// </summary>
internal record RotuloJson(
    bool EhTabelaNutricional,
    string? Nome,
    decimal PorcaoG,
    decimal Kcal,
    decimal ProteinaG,
    decimal CarboG,
    decimal GorduraG,
    decimal FibraG,
    List<string>? Alertas)
{
    public const string Prompt =
        "Você lê a tabela nutricional (\"Informação Nutricional\") de rótulos brasileiros de alimentos e suplementos. " +
        "Copie os números exatamente como aparecem, sem estimar. Se a tabela tiver uma coluna por 100 g, use essa coluna " +
        "e ponha porcaoG = 100; senão use a coluna da porção e ponha em porcaoG a porção em gramas (\"Porção de 10 g\" = 10; " +
        "para líquidos, ml como gramas). Campos: kcal (valor energético em kcal, não kJ), proteinaG, carboG (carboidratos " +
        "totais), gorduraG (gorduras totais), fibraG. Em nome, o nome do produto e o sabor se aparecerem na foto. " +
        "O que não aparecer: 0 nos números e \"\" nos textos; \"0\", \"zero\" ou \"não contém\" é 0. Se a imagem não mostrar " +
        "uma tabela nutricional, marque ehTabelaNutricional como false. Use alertas para o que ficou ilegível ou ambíguo. " +
        "Responda somente no JSON do schema.";

    public const string Texto = "Leia a tabela nutricional desta foto.";

    public AnaliseRotuloResultado ParaResultado() => new(
        EhTabelaNutricional,
        string.IsNullOrWhiteSpace(Nome) ? null : Nome.Trim(),
        PorcaoG > 0 ? PorcaoG : null,
        NaoNegativo(Kcal), NaoNegativo(ProteinaG), NaoNegativo(CarboG), NaoNegativo(GorduraG),
        FibraG > 0 ? FibraG : null,
        Alertas ?? []);

    private static decimal? NaoNegativo(decimal v) => v >= 0 ? v : null;
}
