using System.Globalization;
using System.Text;

namespace Extrato.Domain.Regras;

/// <summary>
/// Normalização e similaridade por trigramas (equivalente ao pg_trgm, feito em memória
/// porque o SQL Server não tem extensão de trigramas).
/// </summary>
public static class TextoBusca
{
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var decomposto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        var ultimoEspaco = false;
        foreach (var c in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) { sb.Append(c); ultimoEspaco = false; }
            else if (!ultimoEspaco && sb.Length > 0) { sb.Append(' '); ultimoEspaco = true; }
        }
        return sb.ToString().TrimEnd();
    }

    public static HashSet<string> Trigramas(string normalizado)
    {
        var set = new HashSet<string>();
        foreach (var palavra in normalizado.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = "  " + palavra + " ";
            for (var i = 0; i + 3 <= p.Length; i++) set.Add(p.Substring(i, 3));
        }
        return set;
    }

    /// <summary>Similaridade de Jaccard entre trigramas (0 a 1), como similarity() do pg_trgm.</summary>
    public static double Similaridade(string a, string b)
    {
        var ta = Trigramas(Normalizar(a));
        var tb = Trigramas(Normalizar(b));
        if (ta.Count == 0 || tb.Count == 0) return 0;
        var inter = ta.Count(tb.Contains);
        return (double)inter / (ta.Count + tb.Count - inter);
    }

    /// <summary>
    /// Pontuação de um alimento para a consulta já normalizada, considerando nome e sinônimos
    /// (também normalizados). Igual = 1; prefixo = 0,9; senão média das similaridades.
    /// Usada pelo servidor e pela busca offline do app.
    /// </summary>
    public static double Pontuar(string consultaNormalizada, IEnumerable<string> termosNormalizados)
    {
        double score = 0;
        foreach (var termo in termosNormalizados)
        {
            double s;
            if (termo == consultaNormalizada) s = 1.0;
            else if (termo.StartsWith(consultaNormalizada + " ", StringComparison.Ordinal)) s = 0.9;
            else s = (Similaridade(consultaNormalizada, termo) + SimilaridadeConsulta(consultaNormalizada, termo)) / 2;
            if (s > score) score = s;
        }
        return score;
    }

    /// <summary>
    /// Similaridade de palavra (word_similarity): quanto da consulta aparece no nome.
    /// Melhor para "arroz" vs "Arroz, tipo 1, cozido".
    /// </summary>
    public static double SimilaridadeConsulta(string consulta, string nome)
    {
        var tc = Trigramas(Normalizar(consulta));
        var tn = Trigramas(Normalizar(nome));
        if (tc.Count == 0 || tn.Count == 0) return 0;
        var inter = tc.Count(tn.Contains);
        return (double)inter / tc.Count;
    }
}
