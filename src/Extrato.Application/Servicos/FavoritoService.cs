using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Extrato.Application.Servicos;

public class FavoritoService(IAppDbContext db, IUsuarioAtual usuario, RefeicaoService refeicoes)
{
    public async Task<List<PorcaoFavorita>> TodosAsync(CancellationToken ct) =>
        await db.Favoritos.Where(f => f.UsuarioId == usuario.UsuarioId)
            .Include(f => f.Itens).ThenInclude(i => i.Alimento)
            .OrderBy(f => f.Nome).ToListAsync(ct);

    public async Task<IReadOnlyList<FavoritoDto>> ListarAsync(CancellationToken ct) =>
        (await TodosAsync(ct)).Select(f => f.ParaDto()).ToList();

    public async Task<FavoritoDto> CriarAsync(NovoFavoritoDto dto, CancellationToken ct)
    {
        if (dto.Itens.Count == 0) throw new ValidacaoException("O favorito precisa de pelo menos um item.");
        var ids = dto.Itens.Select(i => i.AlimentoId).Distinct().ToList();
        var mapa = await db.Alimentos.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        if (mapa.Count != ids.Count) throw new ValidacaoException("Há alimentos inexistentes no favorito.");

        var f = new PorcaoFavorita
        {
            UsuarioId = usuario.UsuarioId,
            Nome = dto.Nome.Trim(),
            TipoSugerido = dto.TipoSugerido,
            Itens = dto.Itens.Select(i => new PorcaoFavoritaItem { AlimentoId = i.AlimentoId, Alimento = mapa[i.AlimentoId], Gramas = i.Gramas }).ToList()
        };
        db.Favoritos.Add(f);
        await db.SaveChangesAsync(ct);
        return f.ParaDto();
    }

    public async Task RemoverAsync(int id, CancellationToken ct)
    {
        var f = await db.Favoritos.FirstOrDefaultAsync(x => x.Id == id && x.UsuarioId == usuario.UsuarioId, ct)
            ?? throw new NaoEncontradoException("Favorito não encontrado.");
        db.Favoritos.Remove(f);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Lança o favorito como refeição (origem Favorito), sem foto.</summary>
    public async Task<RefeicaoDto> RegistrarAsync(int id, TipoRefeicao tipo, CancellationToken ct)
    {
        var f = (await TodosAsync(ct)).FirstOrDefault(x => x.Id == id) ?? throw new NaoEncontradoException("Favorito não encontrado.");
        return await refeicoes.CriarAsync(new NovaRefeicaoDto
        {
            Tipo = tipo,
            Origem = OrigemRefeicao.Favorito,
            Descricao = f.Nome,
            Itens = f.Itens.Select(i => new NovoItemRefeicaoDto { AlimentoId = i.AlimentoId, Gramas = i.Gramas }).ToList()
        }, ct);
    }
}
