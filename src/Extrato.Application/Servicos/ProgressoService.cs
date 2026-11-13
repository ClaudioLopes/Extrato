using Extrato.Contracts;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;
using Microsoft.EntityFrameworkCore;

namespace Extrato.Application.Servicos;

public class ProgressoService(IAppDbContext db, IUsuarioAtual usuario, IRelogio relogio, PerfilService perfil)
{
    public async Task<PontoPesoDto> RegistrarPesoAsync(NovoPesoDto dto, CancellationToken ct)
    {
        var data = dto.Data ?? relogio.Hoje;
        if (data > relogio.Hoje) throw new ValidacaoException("Data no futuro.");
        var reg = await db.Pesos.FirstOrDefaultAsync(p => p.UsuarioId == usuario.UsuarioId && p.Data == data, ct);
        if (reg is null) db.Pesos.Add(new RegistroPeso { UsuarioId = usuario.UsuarioId, Data = data, PesoKg = dto.PesoKg });
        else reg.PesoKg = dto.PesoKg;
        await db.SaveChangesAsync(ct);

        var pesos = await PesosAsync(data.AddDays(-6), data, ct);
        return new PontoPesoDto(data, dto.PesoKg, MediaMovel.Em(pesos, data));
    }

    public async Task<MedidaDto> RegistrarMedidasAsync(NovasMedidasDto dto, CancellationToken ct)
    {
        var u = await perfil.ObterUsuarioAsync(ct);
        var data = dto.Data ?? relogio.Hoje;
        if (u.Sexo == Domain.Sexo.Feminino && dto.QuadrilCm is null)
            throw new ValidacaoException("Informe o quadril (necessário no método da Marinha para mulheres).");
        var m = new Medidas
        {
            UsuarioId = u.Id, Data = data, CinturaCm = dto.CinturaCm, PescocoCm = dto.PescocoCm,
            QuadrilCm = dto.QuadrilCm, ObservacaoCorporal = dto.ObservacaoCorporal
        };
        db.Medidas.Add(m);
        await db.SaveChangesAsync(ct);
        return ParaDto(m, u);
    }

    public async Task<ProgressoDto> ObterAsync(DateOnly? de, DateOnly? ate, CancellationToken ct)
    {
        var fim = ate ?? relogio.Hoje;
        var inicio = de ?? fim.AddDays(-60);
        if (inicio > fim) throw new ValidacaoException("Intervalo inválido.");
        var u = await perfil.ObterUsuarioAsync(ct);

        var pesos = await PesosAsync(inicio.AddDays(-6), fim, ct);
        var porData = pesos.ToDictionary(p => p.Data, p => p.PesoKg);
        var pontos = new List<PontoPesoDto>();
        for (var d = inicio; d <= fim; d = d.AddDays(1))
        {
            var mm = MediaMovel.Em(pesos, d);
            porData.TryGetValue(d, out var peso);
            if (mm is null && !porData.ContainsKey(d)) continue;
            pontos.Add(new PontoPesoDto(d, porData.ContainsKey(d) ? peso : null, mm));
        }

        var medidas = await db.Medidas.Where(m => m.UsuarioId == u.Id && m.Data >= inicio && m.Data <= fim)
            .OrderBy(m => m.Data).ToListAsync(ct);
        var ajustes = await db.AjustesMeta.Where(a => a.UsuarioId == u.Id && a.Data >= inicio && a.Data <= fim)
            .OrderByDescending(a => a.Data).ToListAsync(ct);

        // GramasSugeridasIA x Gramas: erro médio absoluto da IA (métrica de portfólio)
        var erros = await db.ItensRefeicao
            .Where(i => i.GramasSugeridasIA != null)
            .Select(i => new { i.Gramas, Sugerida = i.GramasSugeridasIA!.Value })
            .ToListAsync(ct);
        decimal? erroMedio = erros.Count == 0 ? null : Math.Round(erros.Average(e => Math.Abs(e.Gramas - e.Sugerida)), 1);

        return new ProgressoDto(pontos, medidas.Select(m => ParaDto(m, u)).ToList(), ajustes.Select(a => a.ParaDto()).ToList(), erroMedio);
    }

    private async Task<List<(DateOnly Data, decimal PesoKg)>> PesosAsync(DateOnly de, DateOnly ate, CancellationToken ct) =>
        (await db.Pesos.Where(p => p.UsuarioId == usuario.UsuarioId && p.Data >= de && p.Data <= ate)
            .OrderBy(p => p.Data).Select(p => new { p.Data, p.PesoKg }).ToListAsync(ct))
        .Select(p => (p.Data, p.PesoKg)).ToList();

    private static MedidaDto ParaDto(Medidas m, Usuario u) =>
        new(m.Id, m.Data, m.CinturaCm, m.PescocoCm, m.QuadrilCm, m.ObservacaoCorporal,
            GorduraCorporal.MarinhaAmericana(u.Sexo, u.AlturaCm, m.CinturaCm, m.PescocoCm, m.QuadrilCm));
}
