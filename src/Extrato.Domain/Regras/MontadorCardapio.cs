namespace Extrato.Domain.Regras;

public enum PapelAlimento { Proteina = 1, Carboidrato = 2, Complemento = 3 }

/// <param name="Preferencia">0 a 1: quanto o usuário costuma comer isso nessa refeição.</param>
/// <param name="GramasPadrao">Porção usada quando o alimento entra como complemento.</param>
public record CandidatoCardapio(int AlimentoId, string Nome, Macros Por100g, PapelAlimento Papel,
    decimal GramasMin, decimal GramasMax, decimal GramasPadrao, decimal Preferencia);

public record ItemCardapio(int AlimentoId, string Nome, decimal Gramas, Macros Macros);

public record OpcaoCardapio(IReadOnlyList<ItemCardapio> Itens, Macros Total, decimal Erro);

/// <summary>
/// Fase 2: cardápio sugerido por refeição. Combina proteína + carboidrato (+ complemento opcional)
/// e resolve as gramas para aproximar proteína e carboidrato do alvo; gordura e kcal entram no erro.
/// Macros sempre da tabela. Determinístico e testável (sem IA).
/// </summary>
public static class MontadorCardapio
{
    public const decimal KcalMinimaRefeicao = 80;
    /// <summary>Quanto a rotina compensa de erro de encaixe (0 = só macros; 1 = só hábito).</summary>
    public const decimal PesoRotina = 0.5m;

    public static PapelAlimento Classificar(Macros por100g)
    {
        var kcalMacros = por100g.ProteinaG * 4 + por100g.CarboG * 4 + por100g.GorduraG * 9;
        if (kcalMacros <= 0) return PapelAlimento.Complemento;
        var p = por100g.ProteinaG * 4 / kcalMacros;
        var c = por100g.CarboG * 4 / kcalMacros;
        if (p >= 0.4m) return PapelAlimento.Proteina;
        if (c >= 0.55m) return PapelAlimento.Carboidrato;
        return PapelAlimento.Complemento;
    }

    public static IReadOnlyList<OpcaoCardapio> Montar(Macros alvo, IReadOnlyList<CandidatoCardapio> candidatos, int maxOpcoes = 3)
    {
        if (alvo.Kcal < KcalMinimaRefeicao) return [];

        IEnumerable<CandidatoCardapio> Top(PapelAlimento papel, int n) =>
            candidatos.Where(c => c.Papel == papel).OrderByDescending(c => c.Preferencia).ThenBy(c => c.Nome).Take(n);

        var proteinas = Top(PapelAlimento.Proteina, 6).ToList();
        var carbos = Top(PapelAlimento.Carboidrato, 6).ToList();
        var complementos = Top(PapelAlimento.Complemento, 4).Cast<CandidatoCardapio?>().Append(null).ToList();
        if (proteinas.Count == 0 || carbos.Count == 0) return [];

        var opcoes = new List<(OpcaoCardapio Opcao, decimal Score, int ProteinaId, bool Habito)>();
        foreach (var p in proteinas)
        foreach (var c in carbos)
        {
            if (p.AlimentoId == c.AlimentoId) continue;
            var habito = p.Preferencia >= LimiarHabito && c.Preferencia >= LimiarHabito;
            foreach (var x in complementos)
            {
                var opcao = Resolver(alvo, p, c, x);
                if (opcao is null) continue;
                // rotina: proteína e carboidrato definem o prato
                var pref = (p.Preferencia + c.Preferencia) / 2;
                opcoes.Add((opcao, opcao.Erro - PesoRotina * pref, p.AlimentoId, habito));
            }
        }

        // 1ª opção: o prato de costume (se houver hábito registrado), ajustado ao alvo de hoje
        var resultado = opcoes.Where(o => o.Habito).OrderBy(o => o.Score).Take(1).ToList();
        // demais: melhores encaixes, no máximo uma opção por fonte de proteína (variedade)
        resultado.AddRange(opcoes.Where(o => resultado.All(r => r.ProteinaId != o.ProteinaId))
            .OrderBy(o => o.Score).GroupBy(o => o.ProteinaId).Select(g => g.First())
            .OrderBy(o => o.Score).Take(maxOpcoes - resultado.Count));
        return resultado.Select(o => o.Opcao).ToList();
    }

    /// <summary>Preferência a partir da qual o alimento conta como hábito naquela refeição.</summary>
    public const decimal LimiarHabito = 0.6m;

    /// <summary>Resolve gp e gc no sistema 2×2 (proteína e carboidrato), com limites de porção.</summary>
    internal static OpcaoCardapio? Resolver(Macros alvo, CandidatoCardapio p, CandidatoCardapio c, CandidatoCardapio? x)
    {
        var gx = x?.GramasPadrao ?? 0;
        var macX = x is null ? Macros.Zero : x.Por100g.Vezes(gx / 100m);
        var resto = alvo - macX;
        if (resto.Kcal <= 0) return null;

        // por grama
        decimal a1 = p.Por100g.ProteinaG / 100, b1 = c.Por100g.ProteinaG / 100;
        decimal a2 = p.Por100g.CarboG / 100, b2 = c.Por100g.CarboG / 100;
        var det = a1 * b2 - b1 * a2;
        decimal gp, gc;
        if (Math.Abs(det) > 0.0001m)
        {
            gp = (resto.ProteinaG * b2 - b1 * resto.CarboG) / det;
            gc = (a1 * resto.CarboG - a2 * resto.ProteinaG) / det;
        }
        else
        {
            gp = a1 > 0 ? resto.ProteinaG / a1 : p.GramasMin;
            gc = b2 > 0 ? (resto.CarboG - a2 * gp) / b2 : c.GramasMin;
        }
        gp = Arredondar(Math.Clamp(gp, p.GramasMin, p.GramasMax));
        gc = Arredondar(Math.Clamp(gc, c.GramasMin, c.GramasMax));

        var itens = new List<ItemCardapio>
        {
            new(p.AlimentoId, p.Nome, gp, p.Por100g.Vezes(gp / 100m)),
            new(c.AlimentoId, c.Nome, gc, c.Por100g.Vezes(gc / 100m)),
        };
        if (x is not null) itens.Add(new(x.AlimentoId, x.Nome, gx, macX));
        var total = Macros.Somar(itens.Select(i => i.Macros));
        return new OpcaoCardapio(itens, total, Erro(alvo, total));
    }

    /// <summary>Erro relativo ponderado: kcal e proteína pesam mais; estourar kcal pesa mais que faltar.</summary>
    public static decimal Erro(Macros alvo, Macros total)
    {
        decimal Rel(decimal a, decimal t, decimal piso) => Math.Abs(t - a) / Math.Max(a, piso);
        var kcal = Rel(alvo.Kcal, total.Kcal, 100) * (total.Kcal > alvo.Kcal ? 2.5m : 1.5m);
        var prot = Rel(alvo.ProteinaG, total.ProteinaG, 10) * 1.5m;
        var carb = Rel(alvo.CarboG, total.CarboG, 10) * 0.75m;
        var gord = Rel(alvo.GorduraG, total.GorduraG, 5) * 0.5m;
        return Math.Round(kcal + prot + carb + gord, 4);
    }

    private static decimal Arredondar(decimal g) => Math.Max(5, Math.Round(g / 5m, MidpointRounding.AwayFromZero) * 5m);
}
