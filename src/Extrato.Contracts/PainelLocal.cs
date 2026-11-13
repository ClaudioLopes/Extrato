using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;

namespace Extrato.Contracts;

/// <summary>
/// Modo offline: recalcula o painel do dia somando ao último painel salvo os lançamentos que ainda
/// estão na fila. Usa o mesmo distribuidor de refeições do servidor (Domain), então o saldo e os
/// alvos das próximas refeições batem com o que o servidor calcularia.
/// </summary>
public static class PainelLocal
{
    public static PainelDiaDto Combinar(
        PainelDiaDto salvo,
        IEnumerable<RefeicaoDto> pendentes,
        RegrasDto? regras,
        DateTime agora,
        DateTime? salvoEm)
    {
        var doDia = pendentes.Where(p => DateOnly.FromDateTime(p.DataHora) == salvo.Data).ToList();
        var lancamentos = salvo.Lancamentos.Concat(doDia).OrderBy(l => l.DataHora).ToList();

        var consumidoPorTipo = lancamentos.GroupBy(l => l.Tipo)
            .ToDictionary(g => g.Key, g => Macros.Somar(g.Select(l => Para(l.Total))));
        var meta = Para(salvo.Meta);
        var consumido = Macros.Somar(consumidoPorTipo.Values);
        var restante = meta - consumido;

        var pesos = regras is null ? PesosRefeicao.Padrao
            : new PesosRefeicao(regras.PesosRefeicao.Cafe, regras.PesosRefeicao.Almoco, regras.PesosRefeicao.Tarde, regras.PesosRefeicao.Noite);
        var piso = regras?.PisoRefeicao ?? 0.5m;
        if (pesos.Soma <= 0) pesos = PesosRefeicao.Padrao;

        var alvos = DistribuidorRefeicoes.Distribuir(meta, pesos, consumidoPorTipo, piso);
        var refeicoes = alvos.Select(a =>
        {
            var anterior = salvo.Refeicoes.FirstOrDefault(r => r.Tipo == a.Tipo);
            return new AlvoRefeicaoDto(a.Tipo, a.Registrada, MacrosDto.De(a.AlvoOriginal), MacrosDto.De(a.Alvo),
                MacrosDto.De(a.Consumido), a.NoPiso, a.Registrada ? [] : anterior?.Sugestoes ?? []);
        }).ToList();

        var alertas = new List<string>();
        var desde = salvoEm is { } s ? $" (dados de {s:dd/MM HH:mm})" : "";
        alertas.Add($"Sem conexão: painel calculado no aparelho{desde}.");
        if (doDia.Count > 0)
            alertas.Add($"{doDia.Count} lançamento(s) deste dia aguardando envio.");
        if (salvo.Data == DateOnly.FromDateTime(agora) &&
            DistribuidorRefeicoes.ProteinaAtrasada(meta.ProteinaG, consumido.ProteinaG, TimeOnly.FromDateTime(agora), regras?.HoraAlertaProteina ?? 18))
            alertas.Add($"Proteína atrasada: {consumido.ProteinaG:0} g de {meta.ProteinaG:0} g até agora.");
        if (restante.Kcal < 0)
            alertas.Add($"Dia acima da meta em {-restante.Kcal:0} kcal.");

        return salvo with
        {
            Consumido = MacrosDto.De(consumido),
            Restante = MacrosDto.De(restante),
            Refeicoes = refeicoes,
            Lancamentos = lancamentos,
            Alertas = alertas,
        };
    }

    /// <summary>Refeição montada no aparelho a partir do catálogo salvo (macros da tabela, como no servidor).</summary>
    public static RefeicaoDto RefeicaoLocal(NovaRefeicaoDto dto, IReadOnlyDictionary<int, AlimentoDto> catalogo, DateTime agora)
    {
        var itens = dto.Itens.Select(i =>
        {
            var a = catalogo.TryGetValue(i.AlimentoId, out var x) ? x : null;
            var m = a is null ? Macros.Zero : Macros.DePorcao(i.Gramas, a.Kcal100g, a.Proteina100g, a.Carbo100g, a.Gordura100g);
            return new ItemRefeicaoDto(0, i.AlimentoId, a?.Nome ?? $"Alimento {i.AlimentoId}", i.Gramas, MacrosDto.De(m),
                i.GramasSugeridasIA, i.ConfiancaIA, i.GramasSugeridasIA.HasValue && i.GramasSugeridasIA != i.Gramas);
        }).ToList();
        var total = Macros.Somar(itens.Select(i => Para(i.Macros)));
        return new RefeicaoDto(0, dto.DataHora ?? agora, dto.Tipo, dto.Origem,
            string.IsNullOrWhiteSpace(dto.Descricao) ? "aguardando envio" : $"{dto.Descricao} · aguardando envio",
            itens, MacrosDto.De(total));
    }

    /// <summary>Busca offline no catálogo salvo: mesma pontuação do servidor.</summary>
    public static List<AlimentoDto> Buscar(IEnumerable<AlimentoCatalogoDto> catalogo, string busca, int limite)
    {
        var q = TextoBusca.Normalizar(busca);
        if (q.Length == 0) return [];
        return catalogo
            .Select(c => (c.Alimento, Score: TextoBusca.Pontuar(q, c.Termos)))
            .Where(x => x.Score >= 0.2)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Alimento.Nome.Length)
            .Take(limite)
            .Select(x => x.Alimento with { Relevancia = Math.Round(x.Score, 3) })
            .ToList();
    }

    private static Macros Para(MacrosDto m) => new(m.Kcal, m.ProteinaG, m.CarboG, m.GorduraG);
}
