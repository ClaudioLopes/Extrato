using Extrato.Domain.Regras;

namespace Extrato.Contracts;

/// <summary>
/// Gramas iniciais ao escolher um alimento na busca manual. Antes tudo entrava com 100 g,
/// e 100 g de creatina ou de whey passava despercebido na revisão. O usuário ainda ajusta o número.
/// </summary>
public static class PorcaoTipica
{
    // termos já normalizados (sem acento; pontuação vira espaço). A ordem importa: o primeiro que aparece
    // no nome vence ("Leite, de vaca, integral, pó" casa com "po" antes de "leite")
    private static readonly (string Termo, decimal Gramas)[] Tabela =
    [
        ("creatina", 5), ("whey", 30), ("albumina", 30), ("proteina isolada", 30), ("colageno", 10),
        ("achocolatado", 20), ("cafe po", 10), ("cafe soluvel", 2), ("leite em po", 20), ("po", 20),
        ("acucar", 10), ("adocante", 1), ("mel", 15), ("sal", 1),
        ("azeite", 10), ("oleo", 10), ("manteiga", 10), ("margarina", 10), ("requeijao", 20), ("maionese", 15),
        ("queijo", 30), ("presunto", 20), ("mortadela", 20), ("peito de peru", 20),
        ("pao", 50), ("biscoito", 30), ("bolacha", 30), ("torrada", 15), ("granola", 30), ("aveia", 30),
        ("ovo", 50), ("leite", 200), ("iogurte", 170), ("suco", 200), ("refrigerante", 350), ("cafe", 50), ("cha", 200),
        ("castanha", 15), ("amendoim", 20), ("chocolate", 25),
    ];

    public static decimal Gramas(string? nomeAlimento)
    {
        var nome = TextoBusca.Normalizar(nomeAlimento);
        foreach (var (termo, gramas) in Tabela)
            if (ContemPalavra(nome, termo)) return gramas;
        return 100;
    }

    /// <summary>Termo no início de uma palavra ("sal" não casa com "salpicão", mas "pao" casa com "pão de queijo").</summary>
    private static bool ContemPalavra(string texto, string termo)
    {
        for (var i = texto.IndexOf(termo, StringComparison.Ordinal); i >= 0; i = texto.IndexOf(termo, i + 1, StringComparison.Ordinal))
        {
            var inicio = i == 0 || !char.IsLetter(texto[i - 1]);
            var fim = i + termo.Length;
            var final = fim == texto.Length || !char.IsLetter(texto[fim]);
            if (inicio && final) return true;
        }
        return false;
    }
}
