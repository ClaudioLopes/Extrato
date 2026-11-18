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
/// e procura as gramas que aproximam kcal, proteína, carboidrato e gordura do alvo ao mesmo tempo.
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

    /// <summary>Proteína que ainda vale sugerir mesmo com a meta de kcal do dia atingida.</summary>
    public const decimal ProteinaMinimaSemKcal = 10;

    public static IReadOnlyList<OpcaoCardapio> Montar(Macros alvo, IReadOnlyList<CandidatoCardapio> candidatos, int maxOpcoes = 3)
    {
        // kcal do dia atingida mas faltando proteína: prato de proteína magra (~5 kcal por g de proteína)
        if (alvo.Kcal < KcalMinimaRefeicao)
        {
            if (alvo.ProteinaG < ProteinaMinimaSemKcal) return [];
            alvo = alvo with { Kcal = Math.Max(alvo.Kcal, alvo.ProteinaG * 5) };
        }

        IEnumerable<CandidatoCardapio> Top(PapelAlimento papel, int n) =>
            candidatos.Where(c => c.Papel == papel).OrderByDescending(c => c.Preferencia).ThenBy(c => c.Nome).Take(n);

        var proteinas = Top(PapelAlimento.Proteina, 6).ToList();
        // null = só proteína (quando o carbo do dia já foi atingido, é o que encaixa melhor)
        var carbos = Top(PapelAlimento.Carboidrato, 6).Cast<CandidatoCardapio?>().Append(null).ToList();
        // acompanhamento: complemento (salada, azeite) ou um segundo carbo (arroz + feijão)
        var complementos = Top(PapelAlimento.Complemento, 4).Concat(Top(PapelAlimento.Carboidrato, 4))
            .Cast<CandidatoCardapio?>().Append(null).ToList();
        if (proteinas.Count == 0) return [];

        var opcoes = new List<(OpcaoCardapio Opcao, decimal Score, int ProteinaId, bool Habito)>();
        foreach (var p in proteinas)
        foreach (var c in carbos)
        {
            if (p.AlimentoId == c?.AlimentoId) continue;
            var habito = p.Preferencia >= LimiarHabito && (c?.Preferencia ?? 0) >= LimiarHabito;
            foreach (var x in complementos)
            {
                if (x is not null && (x.AlimentoId == c?.AlimentoId || x.AlimentoId == p.AlimentoId)) continue;
                var opcao = Resolver(alvo, p, c, x);
                if (opcao is null) continue;
                // rotina: proteína e carboidrato definem o prato
                var pref = (p.Preferencia + (c?.Preferencia ?? 0)) / 2;
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

    /// <summary>
    /// Procura as gramas (passos de 5 g, dentro dos limites de cada porção) que deixam kcal, proteína,
    /// carboidrato e gordura juntos mais perto do alvo, pelo mesmo <see cref="Erro"/> que ordena as opções.
    /// O complemento (salada, azeite…) entra com 0,5× a 2× a porção padrão. Sem carbo (c null): só proteína.
    /// </summary>
    internal static OpcaoCardapio? Resolver(Macros alvo, CandidatoCardapio p, CandidatoCardapio? c, CandidatoCardapio? x)
    {
        static (double K, double P, double C, double G) PorGrama(CandidatoCardapio a) =>
            ((double)a.Por100g.Kcal / 100, (double)a.Por100g.ProteinaG / 100, (double)a.Por100g.CarboG / 100, (double)a.Por100g.GorduraG / 100);

        var mp = PorGrama(p);
        var mc = c is null ? default : PorGrama(c);
        var mx = x is null ? default : PorGrama(x);
        var gramasX = x is null ? [0m] : new[] { 0.5m, 1m, 1.5m, 2m }.Select(f => Arredondar(x.GramasPadrao * f)).Distinct().ToArray();
        var gramasC = c is null ? [0m] : Faixa(c);
        var gramasP = Faixa(p);
        var a = ((double)alvo.Kcal, (double)alvo.ProteinaG, (double)alvo.CarboG, (double)alvo.GorduraG);

        double melhor = double.MaxValue;
        decimal bp = 0, bc = 0, bx = 0;
        foreach (var gx in gramasX)
        foreach (var gc in gramasC)
        foreach (var gp in gramasP)
        {
            double dp = (double)gp, dc = (double)gc, dx = (double)gx;
            var erro = ErroRapido(a,
                mp.K * dp + mc.K * dc + mx.K * dx, mp.P * dp + mc.P * dc + mx.P * dx,
                mp.C * dp + mc.C * dc + mx.C * dx, mp.G * dp + mc.G * dc + mx.G * dx);
            if (erro < melhor) { melhor = erro; bp = gp; bc = gc; bx = gx; }
        }

        var itens = new List<ItemCardapio> { new(p.AlimentoId, p.Nome, bp, p.Por100g.Vezes(bp / 100m)) };
        if (c is not null) itens.Add(new(c.AlimentoId, c.Nome, bc, c.Por100g.Vezes(bc / 100m)));
        if (x is not null) itens.Add(new(x.AlimentoId, x.Nome, bx, x.Por100g.Vezes(bx / 100m)));
        var total = Macros.Somar(itens.Select(i => i.Macros));
        return new OpcaoCardapio(itens, total, Erro(alvo, total));
    }

    private static decimal[] Faixa(CandidatoCardapio a)
    {
        var min = Arredondar(a.GramasMin);
        var max = Math.Max(min, Arredondar(a.GramasMax));
        var lista = new List<decimal>();
        for (var g = min; g <= max; g += 5) lista.Add(g);
        return [.. lista];
    }

    private static double ErroRapido((double K, double P, double C, double G) a, double k, double p, double c, double g)
    {
        static double Rel(double alvo, double t, double piso) => Math.Abs(t - alvo) / Math.Max(alvo, piso);
        // pisos: com alvo pequeno (ex.: 6 g de gordura no fim do dia) poucos gramas a mais não podem valer
        // mais que a proteína que falta
        return Rel(a.K, k, 100) * (k > a.K ? PesoKcalAcima : PesoKcalAbaixo) + Rel(a.P, p, 10) * PesoProteina
             + Rel(a.C, c, 20) * PesoCarbo + Rel(a.G, g, 15) * PesoGordura;
    }

    private const double PesoKcalAcima = 2.5, PesoKcalAbaixo = 1.5, PesoProteina = 1.5, PesoCarbo = 1.0, PesoGordura = 1.0;

    /// <summary>Erro relativo ponderado dos quatro macros: kcal e proteína pesam mais; estourar kcal pesa mais que faltar.</summary>
    public static decimal Erro(Macros alvo, Macros total) =>
        Math.Round((decimal)ErroRapido(((double)alvo.Kcal, (double)alvo.ProteinaG, (double)alvo.CarboG, (double)alvo.GorduraG),
            (double)total.Kcal, (double)total.ProteinaG, (double)total.CarboG, (double)total.GorduraG), 4);

    private static decimal Arredondar(decimal g) => Math.Max(5, Math.Round(g / 5m, MidpointRounding.AwayFromZero) * 5m);
}
