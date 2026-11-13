using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Extrato.Application.Servicos;

public class RefeicaoService(
    IAppDbContext db,
    IUsuarioAtual usuario,
    IRelogio relogio,
    IVisionProvider vision,
    IProcessadorImagem processador,
    AlimentoService alimentos,
    ILogger<RefeicaoService> logger)
{
    public const decimal ConfiancaBaixa = 0.5m;

    /// <summary>
    /// Seção 5.2. Nada é gravado: a imagem vive só neste escopo e o rascunho volta ao cliente.
    /// </summary>
    public async Task<RascunhoRefeicaoDto> AnalisarFotoAsync(Stream imagem, TipoRefeicao tipo, string? descricao, CancellationToken ct)
    {
        // imagem inválida → 400 (ValidacaoException sobe); só falhas da IA caem no fallback manual
        await using var preparada = await processador.PrepararAsync(imagem, ct);
        AnalisePratoResultado analise;
        try
        {
            analise = await vision.AnalisarPratoAsync(preparada, descricao, tipo, ct);
            ValidarAnalise(analise);
        }
        catch (VisionIndisponivelException ex)
        {
            // nunca logar conteúdo/base64 da imagem: só o motivo
            logger.LogWarning("IA de visão indisponível ({Provedor}): {Motivo}", vision.Nome, ex.Message);
            return Falha(tipo, descricao, ex.Message);
        }
        catch (ValidacaoException ex)
        {
            logger.LogWarning("Resposta da IA fora do contrato: {Motivo}", ex.Message);
            return Falha(tipo, descricao, "A IA respondeu fora do formato esperado. " + ex.Message);
        }

        var evitados = await TermosEvitadosAsync(ct);
        var itens = new List<ItemRascunhoDto>();
        foreach (var item in analise.Itens)
        {
            var match = await alimentos.MatchAsync(item.Nome, item.Preparo, ct);
            var macros = match?.Alimento.MacrosPara(item.Gramas);
            var evitado = EhEvitado(evitados, item.Nome, match?.Alimento);
            itens.Add(new ItemRascunhoDto(
                item.Nome, item.Preparo, item.Gramas, item.Confianca, item.Observacao,
                match?.Alimento.Id, match?.Alimento.Nome,
                macros is { } m ? MacrosDto.De(m) : null,
                NaoIdentificado: match is null,
                BaixaConfianca: item.Confianca < ConfiancaBaixa,
                Evitado: evitado));
        }

        var alertas = analise.Alertas.ToList();
        if (itens.Any(i => i.Evitado))
            alertas.Add("Há itens que você evita: provável erro de identificação, confira.");

        return new RascunhoRefeicaoDto(tipo, descricao, itens, alertas, false, null, vision.Nome);
    }

    /// <summary>Seção 9.4: gramas 1–1.500, confiança 0–1. Fora disso → fallback manual.</summary>
    internal static void ValidarAnalise(AnalisePratoResultado analise)
    {
        if (analise.Itens is null) throw new ValidacaoException("Lista de itens ausente.");
        foreach (var i in analise.Itens)
        {
            if (string.IsNullOrWhiteSpace(i.Nome)) throw new ValidacaoException("Item sem nome.");
            if (i.Gramas is < 1 or > 1500) throw new ValidacaoException($"Gramas fora da faixa para '{i.Nome}'.");
            if (i.Confianca is < 0 or > 1) throw new ValidacaoException($"Confiança fora da faixa para '{i.Nome}'.");
        }
    }

    public async Task<IReadOnlyList<RefeicaoDto>> ListarDoDiaAsync(DateOnly data, CancellationToken ct) =>
        (await DoDiaAsync(data, ct)).Select(r => r.ParaDto()).ToList();

    internal async Task<List<Refeicao>> DoDiaAsync(DateOnly data, CancellationToken ct)
    {
        var ini = data.ToDateTime(TimeOnly.MinValue);
        var fim = ini.AddDays(1);
        return await db.Refeicoes.Where(r => r.UsuarioId == usuario.UsuarioId && r.DataHora >= ini && r.DataHora < fim)
            .Include(r => r.Itens).ThenInclude(i => i.Alimento)
            .OrderBy(r => r.DataHora).ToListAsync(ct);
    }

    /// <summary>Grava a refeição confirmada. Macros sempre recalculados aqui a partir da tabela.</summary>
    public async Task<RefeicaoDto> CriarAsync(NovaRefeicaoDto dto, CancellationToken ct)
    {
        if (dto.IdCliente is { } idCliente)
        {
            var existente = await db.Refeicoes.Include(r => r.Itens).ThenInclude(i => i.Alimento)
                .FirstOrDefaultAsync(r => r.UsuarioId == usuario.UsuarioId && r.IdCliente == idCliente, ct);
            if (existente is not null) return existente.ParaDto();
        }

        var refeicao = new Refeicao
        {
            IdCliente = dto.IdCliente,
            UsuarioId = usuario.UsuarioId,
            DataHora = dto.DataHora ?? relogio.Agora,
            Tipo = dto.Tipo,
            Origem = dto.Origem,
            DescricaoUsuario = dto.Descricao
        };
        await PreencherItensAsync(refeicao, dto.Itens, ct);
        db.Refeicoes.Add(refeicao);
        await db.SaveChangesAsync(ct);
        return refeicao.ParaDto();
    }

    public async Task<RefeicaoDto> EditarAsync(int id, NovaRefeicaoDto dto, CancellationToken ct)
    {
        var refeicao = await db.Refeicoes.Include(r => r.Itens)
            .FirstOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuario.UsuarioId, ct)
            ?? throw new NaoEncontradoException("Refeição não encontrada.");
        refeicao.Tipo = dto.Tipo;
        refeicao.DescricaoUsuario = dto.Descricao;
        if (dto.DataHora.HasValue) refeicao.DataHora = dto.DataHora.Value;
        db.ItensRefeicao.RemoveRange(refeicao.Itens);
        refeicao.Itens = [];
        await PreencherItensAsync(refeicao, dto.Itens, ct);
        await db.SaveChangesAsync(ct);
        return refeicao.ParaDto();
    }

    public async Task RemoverAsync(int id, CancellationToken ct)
    {
        var refeicao = await db.Refeicoes.FirstOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuario.UsuarioId, ct)
            ?? throw new NaoEncontradoException("Refeição não encontrada.");
        db.Refeicoes.Remove(refeicao);
        await db.SaveChangesAsync(ct);
    }

    private async Task PreencherItensAsync(Refeicao refeicao, List<NovoItemRefeicaoDto> itens, CancellationToken ct)
    {
        if (itens.Count == 0) throw new ValidacaoException("A refeição precisa de pelo menos um item.");
        var ids = itens.Select(i => i.AlimentoId).Distinct().ToList();
        var mapa = await db.Alimentos.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var i in itens)
        {
            if (!mapa.TryGetValue(i.AlimentoId, out var alimento))
                throw new ValidacaoException($"Alimento {i.AlimentoId} não existe.");
            if (i.Gramas is <= 0 or > 1500) throw new ValidacaoException("Gramas devem estar entre 1 e 1.500.");
            var item = new ItemRefeicao
            {
                AlimentoId = alimento.Id, Alimento = alimento, Gramas = i.Gramas,
                GramasSugeridasIA = i.GramasSugeridasIA, ConfiancaIA = i.ConfiancaIA
            };
            item.Calcular(alimento);
            refeicao.Itens.Add(item);
        }
    }

    private async Task<List<AlimentoEvitado>> TermosEvitadosAsync(CancellationToken ct) =>
        await db.AlimentosEvitados.Where(e => e.UsuarioId == usuario.UsuarioId).ToListAsync(ct);

    internal static bool EhEvitado(IEnumerable<AlimentoEvitado> evitados, string nomeIA, Alimento? alimento)
    {
        var nomeNorm = TextoBusca.Normalizar(nomeIA);
        foreach (var e in evitados)
        {
            if (alimento != null && e.AlimentoId == alimento.Id) return true;
            if (e.Termo is { Length: > 0 } t)
            {
                var palavras = nomeNorm.Split(' ').Concat((alimento?.NomeNormalizado ?? "").Split(' '));
                if (palavras.Any(p => p == t || (t.Length >= 4 && p.StartsWith(t, StringComparison.Ordinal)))) return true;
            }
        }
        return false;
    }

    private RascunhoRefeicaoDto Falha(TipoRefeicao tipo, string? descricao, string motivo) =>
        new(tipo, descricao, [], [], true, motivo, vision.Nome);
}
