using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;

namespace Extrato.Application.Servicos;

/// <summary>Seção 5.4: o "saldo" do dia.</summary>
public class PainelService(IRelogio relogio, MetaService metas, PerfilService perfil, RefeicaoService refeicoes, FavoritoService favoritos)
{
    public async Task<PainelDiaDto> DiaAsync(DateOnly? data, CancellationToken ct)
    {
        var dia = data ?? relogio.Hoje;
        var metaDia = await metas.MetaDoDiaAsync(dia, ct) ?? throw new NaoEncontradoException("Calcule a meta antes de usar o painel.");
        var regras = await perfil.ObterRegrasAsync(ct);
        var lancamentos = await refeicoes.DoDiaAsync(dia, ct);

        var consumidoPorTipo = lancamentos.GroupBy(r => r.Tipo)
            .ToDictionary(g => g.Key, g => Macros.Somar(g.Select(r => r.Total())));
        var consumido = Macros.Somar(consumidoPorTipo.Values);
        var metaMacros = metaDia.Macros;
        var alvos = DistribuidorRefeicoes.Distribuir(metaMacros, regras.PesosRefeicao, consumidoPorTipo, regras.PisoRefeicao);

        var favs = await favoritos.TodosAsync(ct);
        var refeicoesDto = alvos.Select(a => new AlvoRefeicaoDto(
            a.Tipo, a.Registrada, MacrosDto.De(a.AlvoOriginal), MacrosDto.De(a.Alvo), MacrosDto.De(a.Consumido), a.NoPiso,
            a.Registrada ? [] : SugerirFavoritos(favs, a.Tipo, a.Alvo))).ToList();

        var alertas = new List<string>();
        var restante = metaMacros - consumido;
        var agora = dia == relogio.Hoje ? TimeOnly.FromDateTime(relogio.Agora) : new TimeOnly(23, 59);
        if (DistribuidorRefeicoes.ProteinaAtrasada(metaMacros.ProteinaG, consumido.ProteinaG, agora, regras.HoraAlertaProteina))
            alertas.Add($"Proteína atrasada: {consumido.ProteinaG:0} g de {metaMacros.ProteinaG:0} g até agora.");
        if (restante.Kcal < 0)
            alertas.Add($"Dia acima da meta em {-restante.Kcal:0} kcal. As próximas refeições foram reduzidas até o piso; não pule refeições.");
        if (alvos.Any(a => a.NoPiso))
            alertas.Add("Algumas refeições estão no piso mínimo do alvo.");

        return new PainelDiaDto(dia, MacrosDto.De(metaMacros), MacrosDto.De(consumido), MacrosDto.De(restante),
            refeicoesDto, lancamentos.Select(r => r.ParaDto()).ToList(), alertas, metaDia.DiaDeTreino, metaDia.Explicacao);
    }

    /// <summary>2–3 favoritos que cabem no alvo da refeição (kcal até +10%), mais próximos primeiro.</summary>
    internal static IReadOnlyList<SugestaoFavoritoDto> SugerirFavoritos(IEnumerable<PorcaoFavorita> favoritos, TipoRefeicao tipo, Macros alvo)
    {
        if (alvo.Kcal <= 0) return [];
        return favoritos
            .Select(f => (Fav: f, Total: f.TotalFavorito()))
            .Where(x => x.Total.Kcal > 0 && x.Total.Kcal <= alvo.Kcal * 1.10m)
            .OrderByDescending(x => x.Fav.TipoSugerido == tipo)
            .ThenBy(x => Math.Abs(alvo.Kcal - x.Total.Kcal) / alvo.Kcal + Math.Abs(alvo.ProteinaG - x.Total.ProteinaG) / Math.Max(alvo.ProteinaG, 1))
            .Take(3)
            .Select(x => new SugestaoFavoritoDto(x.Fav.Id, x.Fav.Nome, MacrosDto.De(x.Total)))
            .ToList();
    }
}
