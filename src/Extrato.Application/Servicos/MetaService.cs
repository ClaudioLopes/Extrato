using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;
using Microsoft.EntityFrameworkCore;

namespace Extrato.Application.Servicos;

public class MetaService(IAppDbContext db, IUsuarioAtual usuario, IRelogio relogio, PerfilService perfil, TreinoService treino)
{
    public async Task<MetaDiaria?> VigenteEmAsync(DateOnly data, CancellationToken ct) =>
        await db.Metas.Where(m => m.UsuarioId == usuario.UsuarioId && m.VigenteDesde <= data)
            .OrderByDescending(m => m.VigenteDesde).ThenByDescending(m => m.Id).FirstOrDefaultAsync(ct);

    /// <summary>Meta efetiva do dia: a base vigente ajustada por dia de treino/descanso (fase 2).</summary>
    public async Task<MetaDoDia?> MetaDoDiaAsync(DateOnly dia, CancellationToken ct)
    {
        var meta = await VigenteEmAsync(dia, ct);
        if (meta is null) return null;
        var regras = await perfil.ObterRegrasAsync(ct);
        var ini = dia.ToDateTime(TimeOnly.MinValue);
        var fim = ini.AddDays(1);
        var houveSessao = await db.Sessoes.AnyAsync(s => s.DataHora >= ini && s.DataHora < fim, ct);
        decimal tmb = 0;
        try
        {
            var p = await perfil.PerfilCalculoAsync(ct);
            tmb = CalculadoraMetas.Tmb(p.Sexo, p.PesoKg, p.AlturaCm, p.Idade);
        }
        catch (ValidacaoException) { /* sem peso registrado: sem trava de TMB */ }
        return CicloTreino.Aplicar(meta.ComoMacros(), regras, CicloTreino.EhDiaDeTreino(dia, regras.DiasTreino, houveSessao), tmb);
    }

    public async Task<MetaDto> AtualAsync(CancellationToken ct) =>
        (await VigenteEmAsync(relogio.Hoje, ct))?.ParaDto() ?? throw new NaoEncontradoException("Nenhuma meta calculada ainda.");

    public async Task<CalculoMetaDto> RecalcularAsync(CancellationToken ct)
    {
        var p = await perfil.PerfilCalculoAsync(ct);
        var regras = await perfil.ObterRegrasAsync(ct);
        var r = CalculadoraMetas.Calcular(p, regras);

        var existeAlguma = await db.Metas.AnyAsync(m => m.UsuarioId == usuario.UsuarioId, ct);
        var meta = new MetaDiaria
        {
            UsuarioId = usuario.UsuarioId,
            VigenteDesde = relogio.Hoje,
            Kcal = r.Kcal,
            ProteinaG = r.ProteinaG,
            CarboG = r.CarboG,
            GorduraG = r.GorduraG,
            Origem = existeAlguma ? OrigemMeta.Manual : OrigemMeta.Inicial
        };
        db.Metas.Add(meta);
        await db.SaveChangesAsync(ct);
        return new CalculoMetaDto(r.Tmb, r.FatorAtividade, r.Get, r.AjusteKcal, meta.ParaDto(), r.Racional);
    }

    /// <summary>Simula o cálculo sem gravar (tela de onboarding mostra o racional antes de confirmar).</summary>
    public async Task<CalculoMetaDto> SimularAsync(CancellationToken ct)
    {
        var p = await perfil.PerfilCalculoAsync(ct);
        var r = CalculadoraMetas.Calcular(p, await perfil.ObterRegrasAsync(ct));
        var meta = new MetaDto(0, relogio.Hoje, r.Kcal, r.ProteinaG, r.CarboG, r.GorduraG, OrigemMeta.Inicial);
        return new CalculoMetaDto(r.Tmb, r.FatorAtividade, r.Get, r.AjusteKcal, meta, r.Racional);
    }

    public async Task<MetaDto> DefinirManualAsync(MetaManualDto dto, CancellationToken ct)
    {
        var p = await perfil.PerfilCalculoAsync(ct);
        var tmb = CalculadoraMetas.Tmb(p.Sexo, p.PesoKg, p.AlturaCm, p.Idade);
        if (dto.Kcal < tmb) throw new ValidacaoException($"Meta abaixo da TMB ({tmb:0} kcal) não é permitida.");
        var regras = await perfil.ObterRegrasAsync(ct);
        if (dto.ProteinaG > regras.Travas.ProteinaMaxGPorKg * p.PesoKg)
            throw new ValidacaoException($"Proteína acima da trava de {regras.Travas.ProteinaMaxGPorKg} g/kg.");

        var meta = new MetaDiaria
        {
            UsuarioId = usuario.UsuarioId, VigenteDesde = relogio.Hoje, Kcal = dto.Kcal,
            ProteinaG = dto.ProteinaG, CarboG = dto.CarboG, GorduraG = dto.GorduraG, Origem = OrigemMeta.Manual
        };
        db.Metas.Add(meta);
        await db.SaveChangesAsync(ct);
        return meta.ParaDto();
    }

    /// <summary>Seção 6.5. Roda pelo endpoint e pelo job semanal.</summary>
    public async Task<AjusteSemanalDto> AjusteSemanalAsync(CancellationToken ct)
    {
        var hoje = relogio.Hoje;
        var meta = await VigenteEmAsync(hoje, ct) ?? throw new NaoEncontradoException("Nenhuma meta para ajustar.");
        var p = await perfil.PerfilCalculoAsync(ct);
        var regras = await perfil.ObterRegrasAsync(ct);
        var uid = usuario.UsuarioId;

        var desde = hoje.AddDays(-21);
        var pesos = await db.Pesos.Where(x => x.UsuarioId == uid && x.Data >= desde && x.Data <= hoje)
            .Select(x => new { x.Data, x.PesoKg }).ToListAsync(ct);

        var inicioSemana = hoje.AddDays(-6).ToDateTime(TimeOnly.MinValue);
        var fim = hoje.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var refeicoes = await db.Refeicoes.Where(r => r.UsuarioId == uid && r.DataHora >= inicioSemana && r.DataHora < fim)
            .Include(r => r.Itens).ToListAsync(ct);
        var ingestao = refeicoes.GroupBy(r => DateOnly.FromDateTime(r.DataHora))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Total().Kcal));

        var ultimoAjuste = await db.AjustesMeta.Where(a => a.UsuarioId == uid)
            .OrderByDescending(a => a.Data).Select(a => (DateOnly?)a.Data).FirstOrDefaultAsync(ct);

        var tmb = CalculadoraMetas.Tmb(p.Sexo, p.PesoKg, p.AlturaCm, p.Idade);
        var leitura = await treino.LeituraParaAjusteAsync(hoje, ct);

        var r = AjusteSemanal.Avaliar(new EntradaAjusteSemanal(
            hoje, p.Objetivo, meta.Kcal, tmb,
            pesos.Select(x => (x.Data, x.PesoKg)).ToList(), ingestao, ultimoAjuste, regras.Travas, leitura));

        if (r.Ajustar)
        {
            // mantém proteína e gordura; a diferença de kcal vai para o carboidrato
            var carbo = Math.Max(0, Math.Round(meta.CarboG + (r.KcalNova - r.KcalAnterior) / Macros.KcalPorGCarbo, 1));
            db.Metas.Add(new MetaDiaria
            {
                UsuarioId = uid, VigenteDesde = hoje, Kcal = r.KcalNova, ProteinaG = meta.ProteinaG,
                GorduraG = meta.GorduraG, CarboG = carbo, Origem = OrigemMeta.Ajuste
            });
            db.AjustesMeta.Add(new AjusteMeta
            {
                UsuarioId = uid, Data = hoje, KcalAnterior = r.KcalAnterior, KcalNova = r.KcalNova,
                GetEstimado = r.GetEstimado, Motivo = r.Motivo
            });
            await db.SaveChangesAsync(ct);
        }

        return new AjusteSemanalDto(r.Ajustar, r.KcalAnterior, r.KcalNova, r.GetEstimado, r.RitmoPercentSemana, r.Motivo);
    }
}
