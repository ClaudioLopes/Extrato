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
    public const decimal PesoRotina = 0.25m;

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
        // acompanhamentos opcionais: um segundo carbo (arroz + feijão) e um complemento (salada, azeite)
        var segundos = Top(PapelAlimento.Carboidrato, 4).Cast<CandidatoCardapio?>().Append(null).ToList();
        var complementos = Top(PapelAlimento.Complemento, 4).Cast<CandidatoCardapio?>().Append(null).ToList();
        if (proteinas.Count == 0) return [];

        var opcoes = new List<(OpcaoCardapio Opcao, decimal Score, string Proteina, bool Habito)>();
        foreach (var p in proteinas)
        foreach (var c in carbos)
        {
            if (p.AlimentoId == c?.AlimentoId) continue;
            var habito = p.Preferencia >= LimiarHabito && (c?.Preferencia ?? 0) >= LimiarHabito;
            foreach (var s2 in segundos)
            foreach (var x in complementos)
            {
                // nada repetido no prato: nem o mesmo alimento, nem a mesma família (arroz branco + arroz integral)
                if (s2 is not null && (c is null || Familia(s2.Nome) == Familia(c.Nome) || Familia(s2.Nome) == Familia(p.Nome))) continue;
                if (x is not null && (Familia(x.Nome) == Familia(c?.Nome) || Familia(x.Nome) == Familia(p.Nome))) continue;
                var opcao = Resolver(alvo, p, c, s2, x);
                if (opcao is null) continue;
                // rotina: proteína e carboidrato definem o prato
                var pref = (p.Preferencia + (c?.Preferencia ?? 0)) / 2;
                opcoes.Add((opcao, opcao.Erro - PesoRotina * pref, Familia(p.Nome), habito));
            }
        }

        // 1ª opção: o prato de costume, ajustado ao alvo de hoje, só se encaixar quase tão bem quanto o melhor
        // (salmão 30 g + arroz, de costume, vinha primeiro com 21 g de proteína de 75)
        var melhorErro = opcoes.Count == 0 ? 0 : opcoes.Min(o => o.Opcao.Erro);
        var resultado = opcoes.Where(o => o.Habito && o.Opcao.Erro <= melhorErro + ToleranciaHabito)
            .OrderBy(o => o.Score).Take(1).ToList();
        // demais: melhores encaixes, no máximo uma opção por fonte de proteína (salmão cru e grelhado contam como uma)
        resultado.AddRange(opcoes.Where(o => resultado.All(r => r.Proteina != o.Proteina))
            .OrderBy(o => o.Score).GroupBy(o => o.Proteina).Select(g => g.First())
            .OrderBy(o => o.Score).Take(maxOpcoes - resultado.Count));
        return resultado.Select(o => o.Opcao).ToList();
    }

    /// <summary>Quanto de erro a mais o prato de costume pode ter e ainda vir em primeiro.</summary>
    public const decimal ToleranciaHabito = 0.3m;

    /// <summary>Primeira parte do nome na tabela ("Arroz, integral, cozido" → "arroz").</summary>
    private static string Familia(string? nome) =>
        nome is null ? "" : TextoBusca.Normalizar(nome.Split(',')[0]);

    /// <summary>Preferência a partir da qual o alimento conta como hábito naquela refeição.</summary>
    public const decimal LimiarHabito = 0.6m;

    /// <summary>
    /// Procura as gramas (passos de 5 g, dentro dos limites de cada porção) que deixam kcal, proteína,
    /// carboidrato e gordura juntos mais perto do alvo, pelo mesmo <see cref="Erro"/> que ordena as opções.
    /// Proteína e carbo: grade de 25 g e depois refino de 5 em 5 g. Acompanhamentos (segundo carbo,
    /// salada, azeite): 0,5× a 2× a porção padrão. Sem carbo (c null): só proteína.
    /// </summary>
    internal static OpcaoCardapio? Resolver(Macros alvo, CandidatoCardapio p, CandidatoCardapio? c,
        CandidatoCardapio? segundo = null, CandidatoCardapio? x = null)
    {
        var slots = new List<(CandidatoCardapio Cand, decimal[] Gramas)> { (p, Faixa(p)) };
        if (c is not null) slots.Add((c, Faixa(c)));
        if (segundo is not null) slots.Add((segundo, Multiplos(segundo)));
        if (x is not null) slots.Add((x, Multiplos(x)));
        var por = slots.Select(s => (K: (double)s.Cand.Por100g.Kcal / 100, P: (double)s.Cand.Por100g.ProteinaG / 100,
            C: (double)s.Cand.Por100g.CarboG / 100, G: (double)s.Cand.Por100g.GorduraG / 100)).ToArray();
        var a = ((double)alvo.Kcal, (double)alvo.ProteinaG, (double)alvo.CarboG, (double)alvo.GorduraG);

        double Avaliar(int[] ix)
        {
            double k = 0, pr = 0, cb = 0, g = 0;
            for (var i = 0; i < ix.Length; i++)
            {
                var gr = (double)slots[i].Gramas[ix[i]];
                k += por[i].K * gr; pr += por[i].P * gr; cb += por[i].C * gr; g += por[i].G * gr;
            }
            return ErroRapido(a, k, pr, cb, g);
        }

        // começo: acompanhamentos na porção padrão (índice 1 de 0,5×/1×/1,5×/2×); grade grossa em proteína e carbo
        var idx = new int[slots.Count];
        for (var i = 2; i < slots.Count; i++) idx[i] = Math.Min(1, slots[i].Gramas.Length - 1);
        var melhorIdx = (int[])idx.Clone();
        var melhor = double.MaxValue;
        for (var i0 = 0; i0 < slots[0].Gramas.Length; i0 += 5)
        for (var i1 = 0; i1 < (slots.Count > 1 ? slots[1].Gramas.Length : 1); i1 += 5)
        {
            idx[0] = i0;
            if (slots.Count > 1) idx[1] = i1;
            var e = Avaliar(idx);
            if (e < melhor) { melhor = e; melhorIdx = (int[])idx.Clone(); }
        }

        // refino: um passo de cada vez em cada alimento enquanto melhorar
        for (var melhorou = true; melhorou;)
        {
            melhorou = false;
            for (var i = 0; i < slots.Count; i++)
            foreach (var d in new[] { -1, 1 })
            {
                var t = (int[])melhorIdx.Clone();
                t[i] += d;
                if (t[i] < 0 || t[i] >= slots[i].Gramas.Length) continue;
                var e = Avaliar(t);
                if (e < melhor - 1e-9) { melhor = e; melhorIdx = t; melhorou = true; }
            }
        }

        var itens = slots.Select((s, i) =>
        {
            var gr = s.Gramas[melhorIdx[i]];
            return new ItemCardapio(s.Cand.AlimentoId, s.Cand.Nome, gr, s.Cand.Por100g.Vezes(gr / 100m));
        }).ToList();
        var total = Macros.Somar(itens.Select(i => i.Macros));
        return new OpcaoCardapio(itens, total, Erro(alvo, total));
    }

    private static decimal[] Multiplos(CandidatoCardapio a) =>
        [.. new[] { 0.5m, 1m, 1.5m, 2m }.Select(f => Arredondar(a.GramasPadrao * f)).Distinct().Order()];

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
