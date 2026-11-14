using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Entidades;

namespace Extrato.Application;

internal static class Mapeamentos
{
    public static MetaDto ParaDto(this MetaDiaria m) =>
        new(m.Id, m.VigenteDesde, m.Kcal, m.ProteinaG, m.CarboG, m.GorduraG, m.Origem);

    public static AlimentoDto ParaDto(this Alimento a, double? relevancia = null) =>
        new(a.Id, a.Nome, a.Fonte, a.Kcal100g, a.Proteina100g, a.Carbo100g, a.Gordura100g, a.Fibra100g, relevancia);

    public static RefeicaoDto ParaDto(this Refeicao r) =>
        new(r.Id, r.DataHora, r.Tipo, r.Origem, r.DescricaoUsuario,
            r.Itens.Select(i => new ItemRefeicaoDto(i.Id, i.AlimentoId, i.Alimento?.Nome ?? "", i.Gramas,
                MacrosDto.De(i.Macros()), i.GramasSugeridasIA, i.ConfiancaIA, i.EditadoPeloUsuario)).ToList(),
            MacrosDto.De(r.Total()));

    public static Macros TotalFavorito(this PorcaoFavorita f) =>
        Macros.Somar(f.Itens.Where(i => i.Alimento != null).Select(i => i.Alimento!.MacrosPara(i.Gramas)));

    public static FavoritoDto ParaDto(this PorcaoFavorita f) =>
        new(f.Id, f.Nome, f.TipoSugerido,
            f.Itens.Select(i => new FavoritoItemDto(i.AlimentoId, i.Alimento?.Nome ?? "", i.Gramas,
                i.Alimento is null ? MacrosDto.Zero : MacrosDto.De(i.Alimento.MacrosPara(i.Gramas)))).ToList(),
            MacrosDto.De(f.TotalFavorito()));

    public static AjusteMetaDto ParaDto(this AjusteMeta a) =>
        new(a.Id, a.Data, a.KcalAnterior, a.KcalNova, a.GetEstimado, a.Motivo);

    public static AparelhoDto ParaDto(this Aparelho a) =>
        new(a.Id, a.AcademiaId, a.Academia?.Nome ?? "", a.Nome, a.Exercicio, a.GrupoMuscular, a.TipoCarga, a.IncrementoKg);

    public static SerieDto ParaDto(this Serie s) =>
        new(s.Id, s.AparelhoId, s.Ordem, s.CargaKg, s.Reps, s.RepsNaReserva, s.IdCliente);

    public static RegrasDto ParaDto(this ConfiguracaoRegras r) => new()
    {
        ProteinaGPorKg = r.ProteinaGPorKg,
        GorduraGPorKg = r.GorduraGPorKg,
        AjusteKcal = r.AjusteKcal,
        FatorAtividade = r.FatorAtividade,
        HoraAlertaProteina = r.HoraAlertaProteina,
        PisoRefeicao = r.PisoRefeicao,
        CicloTreinoAtivo = r.CicloTreinoAtivo,
        KcalExtraDiaTreino = r.KcalExtraDiaTreino,
        DiasTreino = [.. r.DiasTreino],
        PesosRefeicao = new PesosRefeicaoDto
        {
            Cafe = r.PesosRefeicao.Cafe, Almoco = r.PesosRefeicao.Almoco,
            Tarde = r.PesosRefeicao.Tarde, Noite = r.PesosRefeicao.Noite
        },
        Travas = new TravasDto
        {
            ProteinaMaxGPorKg = r.Travas.ProteinaMaxGPorKg,
            VariacaoMaxKcalSemana = r.Travas.VariacaoMaxKcalSemana,
            SuperavitMaxKcal = r.Travas.SuperavitMaxKcal,
            DeficitMaxKcal = r.Travas.DeficitMaxKcal
        }
    };
}
