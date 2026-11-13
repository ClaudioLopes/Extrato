using Extrato.Domain.Entidades;

namespace Extrato.Domain.Regras;

public record AlvoRefeicao(TipoRefeicao Tipo, bool Registrada, Macros AlvoOriginal, Macros Alvo, Macros Consumido, bool NoPiso);

/// <summary>
/// Seção 6.4: a meta do dia não muda; o restante (meta − consumido) é redistribuído
/// entre as refeições que faltam, proporcional aos pesos, com piso. Nunca sugere pular refeição.
/// </summary>
public static class DistribuidorRefeicoes
{
    public static readonly TipoRefeicao[] Ordem =
        [TipoRefeicao.CafeDaManha, TipoRefeicao.Almoco, TipoRefeicao.Tarde, TipoRefeicao.Noite];

    public static IReadOnlyList<AlvoRefeicao> Distribuir(
        Macros metaDia,
        PesosRefeicao pesos,
        IReadOnlyDictionary<TipoRefeicao, Macros> consumidoPorTipo,
        decimal piso = 0.5m)
    {
        if (pesos.Soma <= 0) throw new ArgumentException("Soma dos pesos deve ser positiva.", nameof(pesos));
        if (piso is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(piso));

        var consumidoTotal = Macros.Somar(consumidoPorTipo.Values);
        var restante = metaDia - consumidoTotal;
        var faltam = Ordem.Where(t => !consumidoPorTipo.ContainsKey(t)).ToList();
        var somaPesosFaltam = faltam.Sum(pesos.Para);

        var resultado = new List<AlvoRefeicao>();
        foreach (var tipo in Ordem)
        {
            var original = metaDia.Vezes(pesos.Para(tipo) / pesos.Soma);
            if (consumidoPorTipo.TryGetValue(tipo, out var consumido))
            {
                resultado.Add(new AlvoRefeicao(tipo, true, original, original, consumido, false));
                continue;
            }

            var fracao = somaPesosFaltam > 0 ? pesos.Para(tipo) / somaPesosFaltam : 0;
            var proporcional = restante.Vezes(fracao);
            var minimo = original.Vezes(piso);
            var alvo = new Macros(
                Math.Max(proporcional.Kcal, minimo.Kcal),
                Math.Max(proporcional.ProteinaG, minimo.ProteinaG),
                Math.Max(proporcional.CarboG, minimo.CarboG),
                Math.Max(proporcional.GorduraG, minimo.GorduraG));
            var noPiso = proporcional.Kcal < minimo.Kcal;
            resultado.Add(new AlvoRefeicao(tipo, false, original, alvo, Macros.Zero, noPiso));
        }

        return resultado;
    }

    /// <summary>Seção 5.4: proteína atrasada (menos de 50% da meta até o fim da tarde).</summary>
    public static bool ProteinaAtrasada(decimal metaProteina, decimal consumida, TimeOnly agora, int horaAlerta = 18) =>
        metaProteina > 0 && agora.Hour >= horaAlerta && consumida < metaProteina * 0.5m;
}
