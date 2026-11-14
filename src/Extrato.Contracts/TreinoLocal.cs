using Extrato.Domain.Regras;

namespace Extrato.Contracts;

/// <summary>Série lançada sem conexão, ainda na fila do aparelho.</summary>
public record SeriePendente(Guid IdCliente, int AparelhoId, decimal CargaKg, int Reps, int? RepsNaReserva, DateTime CriadaEm);

/// <summary>
/// Modo offline do treino: monta no aparelho a sessão iniciada sem internet e soma as séries que ainda
/// estão na fila, com as mesmas métricas do servidor (Domain). Séries pendentes têm Id 0 e o IdCliente da fila.
/// </summary>
public static class TreinoLocal
{
    /// <summary>
    /// Sessão iniciada offline, como o servidor a devolveria: aparelhos da rotina (na ordem) com o alvo
    /// e as séries da última sessão de cada um para pré-preencher. Id 0 até ser enviada.
    /// </summary>
    public static SessaoDto NovaSessao(
        DateTime dataHora,
        RotinaDto? rotina,
        IReadOnlyCollection<AparelhoDto> aparelhos,
        IReadOnlyDictionary<int, List<SerieDto>> ultimas,
        string? observacao = null)
    {
        var porId = aparelhos.ToDictionary(a => a.Id);
        var exercicios = (rotina?.Itens ?? []).OrderBy(i => i.Ordem)
            .Where(i => porId.ContainsKey(i.AparelhoId))
            .Select(i => Exercicio(porId[i.AparelhoId], ultimas, i.SeriesAlvo, i.RepsMin, i.RepsMax))
            .ToList();
        return new SessaoDto(0, dataHora, rotina?.Id, rotina?.Nome, observacao, exercicios);
    }

    /// <summary>
    /// Acrescenta as séries pendentes à sessão (na ordem em que foram feitas) e recalcula 1RM e volume do dia.
    /// Aparelho que só aparece numa série pendente (avulso adicionado offline) entra no fim da lista.
    /// </summary>
    public static SessaoDto Combinar(
        SessaoDto sessao,
        IEnumerable<SeriePendente> pendentes,
        IReadOnlyCollection<AparelhoDto> aparelhos,
        IReadOnlyDictionary<int, List<SerieDto>> ultimas)
    {
        var lista = pendentes.OrderBy(p => p.CriadaEm).ToList();
        if (lista.Count == 0) return sessao;

        var exercicios = sessao.Exercicios.ToList();
        var porId = aparelhos.ToDictionary(a => a.Id);
        foreach (var aparelhoId in lista.Select(p => p.AparelhoId).Distinct())
            if (exercicios.All(e => e.Aparelho.Id != aparelhoId) && porId.TryGetValue(aparelhoId, out var aparelho))
                exercicios.Add(Exercicio(aparelho, ultimas, null, null, null));

        exercicios = exercicios.Select(e =>
        {
            var doAparelho = lista.Where(p => p.AparelhoId == e.Aparelho.Id).ToList();
            if (doAparelho.Count == 0) return e;
            var hoje = e.SeriesHoje.ToList();
            foreach (var p in doAparelho)
                hoje.Add(new SerieDto(0, p.AparelhoId, hoje.Count + 1, p.CargaKg, p.Reps, p.RepsNaReserva, p.IdCliente));
            var registradas = hoje.Select(s => new SerieRegistrada(s.CargaKg, s.Reps)).ToList();
            return e with
            {
                SeriesHoje = hoje,
                Melhor1RmHoje = registradas.Max(s => MetricasTreino.Epley(s.CargaKg, s.Reps)),
                VolumeHoje = MetricasTreino.Volume(registradas)
            };
        }).ToList();

        return sessao with { Exercicios = exercicios };
    }

    private static ExercicioSessaoDto Exercicio(
        AparelhoDto aparelho, IReadOnlyDictionary<int, List<SerieDto>> ultimas, int? seriesAlvo, int? repsMin, int? repsMax)
    {
        var anterior = ultimas.TryGetValue(aparelho.Id, out var u) ? u : [];
        var registradas = anterior.Select(s => new SerieRegistrada(s.CargaKg, s.Reps)).ToList();
        return new ExercicioSessaoDto(aparelho, seriesAlvo, repsMin, repsMax, [], anterior, null,
            registradas.Count > 0 ? registradas.Max(s => MetricasTreino.Epley(s.CargaKg, s.Reps)) : null,
            0, MetricasTreino.Volume(registradas));
    }
}
