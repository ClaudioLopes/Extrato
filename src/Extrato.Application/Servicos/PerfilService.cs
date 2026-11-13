using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;
using Microsoft.EntityFrameworkCore;

namespace Extrato.Application.Servicos;

public class PerfilService(IAppDbContext db, IUsuarioAtual usuario, IRelogio relogio)
{
    public async Task<Usuario> ObterUsuarioAsync(CancellationToken ct) =>
        await db.Usuarios.Include(u => u.AlimentosEvitados).FirstOrDefaultAsync(u => u.Id == usuario.UsuarioId, ct)
        ?? throw new NaoEncontradoException("Perfil não cadastrado.");

    public async Task<ConfiguracaoRegras> ObterRegrasAsync(CancellationToken ct)
    {
        var regras = await db.Regras.FirstOrDefaultAsync(r => r.UsuarioId == usuario.UsuarioId, ct);
        if (regras is null)
        {
            regras = ConfiguracaoRegras.PadraoPara(usuario.UsuarioId);
            db.Regras.Add(regras);
            await db.SaveChangesAsync(ct);
        }
        return regras;
    }

    public async Task<decimal?> PesoAtualAsync(CancellationToken ct) =>
        await db.Pesos.Where(p => p.UsuarioId == usuario.UsuarioId)
            .OrderByDescending(p => p.Data).Select(p => (decimal?)p.PesoKg).FirstOrDefaultAsync(ct);

    public async Task<PerfilDto> ObterAsync(CancellationToken ct)
    {
        var u = await ObterUsuarioAsync(ct);
        var nomesEvitados = await NomesEvitadosAsync(u, ct);
        return new PerfilDto
        {
            Nome = u.Nome,
            Sexo = u.Sexo,
            DataNascimento = u.DataNascimento,
            AlturaCm = u.AlturaCm,
            NivelAtividade = u.NivelAtividade,
            Objetivo = u.Objetivo,
            PesoAtualKg = await PesoAtualAsync(ct),
            AlimentosEvitados = nomesEvitados
        };
    }

    public async Task<PerfilDto> AtualizarAsync(PerfilDto dto, CancellationToken ct)
    {
        var u = await db.Usuarios.Include(x => x.AlimentosEvitados).FirstOrDefaultAsync(x => x.Id == usuario.UsuarioId, ct);
        if (u is null)
        {
            u = new Usuario { Id = usuario.UsuarioId };
            db.Usuarios.Add(u);
        }

        if (dto.DataNascimento >= relogio.Hoje) throw new ValidacaoException("Data de nascimento inválida.");

        u.Nome = dto.Nome.Trim();
        u.Sexo = dto.Sexo;
        u.DataNascimento = dto.DataNascimento;
        u.AlturaCm = dto.AlturaCm;
        u.NivelAtividade = dto.NivelAtividade;
        u.Objetivo = dto.Objetivo;

        // alimentos evitados guardados como termo normalizado (ex.: "massa", "figado")
        var termos = dto.AlimentosEvitados.Select(TextoBusca.Normalizar).Where(t => t.Length > 0).Distinct().ToList();
        db.AlimentosEvitados.RemoveRange(u.AlimentosEvitados);
        u.AlimentosEvitados = termos.Select(t => new AlimentoEvitado { UsuarioId = u.Id, Termo = t }).ToList();

        if (dto.PesoAtualKg is { } peso)
        {
            var hoje = relogio.Hoje;
            var registro = await db.Pesos.FirstOrDefaultAsync(p => p.UsuarioId == u.Id && p.Data == hoje, ct);
            if (registro is null) db.Pesos.Add(new RegistroPeso { UsuarioId = u.Id, Data = hoje, PesoKg = peso });
            else registro.PesoKg = peso;
        }

        await db.SaveChangesAsync(ct);
        return await ObterAsync(ct);
    }

    public async Task<RegrasDto> ObterRegrasDtoAsync(CancellationToken ct) => (await ObterRegrasAsync(ct)).ParaDto();

    public async Task<RegrasDto> AtualizarRegrasAsync(RegrasDto dto, CancellationToken ct)
    {
        var p = dto.PesosRefeicao;
        if (p.Cafe + p.Almoco + p.Tarde + p.Noite <= 0) throw new ValidacaoException("A soma dos pesos das refeições deve ser positiva.");
        if (dto.ProteinaGPorKg > dto.Travas.ProteinaMaxGPorKg)
            throw new ValidacaoException($"Proteína acima da trava de {dto.Travas.ProteinaMaxGPorKg} g/kg.");

        var r = await ObterRegrasAsync(ct);
        r.ProteinaGPorKg = dto.ProteinaGPorKg;
        r.GorduraGPorKg = dto.GorduraGPorKg;
        r.AjusteKcal = dto.AjusteKcal;
        r.FatorAtividade = dto.FatorAtividade;
        r.HoraAlertaProteina = dto.HoraAlertaProteina;
        r.PisoRefeicao = dto.PisoRefeicao;
        r.PesosRefeicao = new PesosRefeicao(p.Cafe, p.Almoco, p.Tarde, p.Noite);
        r.Travas = new Travas
        {
            ProteinaMaxGPorKg = dto.Travas.ProteinaMaxGPorKg,
            VariacaoMaxKcalSemana = dto.Travas.VariacaoMaxKcalSemana,
            SuperavitMaxKcal = dto.Travas.SuperavitMaxKcal,
            DeficitMaxKcal = dto.Travas.DeficitMaxKcal
        };
        await db.SaveChangesAsync(ct);
        return r.ParaDto();
    }

    public async Task<PerfilCalculo> PerfilCalculoAsync(CancellationToken ct)
    {
        var u = await ObterUsuarioAsync(ct);
        var peso = await PesoAtualAsync(ct) ?? throw new ValidacaoException("Registre o peso atual para calcular as metas.");
        return new PerfilCalculo(u.Sexo, peso, u.AlturaCm, u.IdadeEm(relogio.Hoje), u.NivelAtividade, u.Objetivo);
    }

    private async Task<List<string>> NomesEvitadosAsync(Usuario u, CancellationToken ct)
    {
        var ids = u.AlimentosEvitados.Where(e => e.AlimentoId.HasValue).Select(e => e.AlimentoId!.Value).ToList();
        var nomes = ids.Count == 0 ? [] : await db.Alimentos.Where(a => ids.Contains(a.Id)).Select(a => a.Nome).ToListAsync(ct);
        return u.AlimentosEvitados.Where(e => e.Termo != null).Select(e => e.Termo!).Concat(nomes).ToList();
    }
}
