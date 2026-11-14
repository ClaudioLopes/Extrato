using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Extrato.Application.Servicos;

/// <summary>
/// Atividades fora da musculação (natação, vôlei…): leitura do print do resumo da pulseira,
/// registro e remoção. As calorias entram na meta do dia pelo MetaService.
/// </summary>
public class AtividadeService(
    IAppDbContext db,
    IUsuarioAtual usuario,
    IRelogio relogio,
    IVisionProvider vision,
    IProcessadorImagem processador,
    PerfilService perfil,
    ILogger<AtividadeService> logger)
{
    /// <summary>Margem para relógios do aparelho e do servidor fora de sincronia.</summary>
    private static readonly TimeSpan ToleranciaFuturo = TimeSpan.FromHours(1);

    /// <summary>Lê o print e devolve um rascunho para conferir. O print não é guardado.</summary>
    public async Task<RascunhoAtividadeDto> AnalisarPrintAsync(Stream imagem, CancellationToken ct)
    {
        await using var preparada = await processador.PrepararAsync(imagem, ct);
        AnaliseAtividadeResultado r;
        try
        {
            r = await vision.AnalisarAtividadeAsync(preparada, ct);
        }
        catch (VisionIndisponivelException ex)
        {
            logger.LogWarning("IA de visão indisponível para atividade: {Motivo}", ex.Message);
            return new RascunhoAtividadeDto(new NovaAtividadeDto { DataHora = relogio.Agora, DuracaoMin = 60 }, false, [], true, ex.Message);
        }

        var alertas = r.Alertas.ToList();
        var tipo = Enum.TryParse<TipoAtividade>(r.Tipo, true, out var t) && Enum.IsDefined(t) ? t : TipoAtividade.Outra;
        var duracao = r.DuracaoSegundos is { } s and > 0 ? Math.Max(1, (int)Math.Round(s / 60m, MidpointRounding.AwayFromZero)) : 0;
        var dataHora = r.DataHora ?? relogio.Agora;
        if (r.DataHora is null) alertas.Add("Data e hora não encontradas no print: usei agora. Confira.");
        else if (dataHora > relogio.Agora + ToleranciaFuturo) alertas.Add("A data do print está no futuro. Confira.");

        var ativas = r.KcalAtivas;
        if (ativas is null && r.KcalTotais is { } totais && duracao > 0)
        {
            var tmb = await TmbAsync(ct);
            ativas = tmb is null ? totais : GastoAtividades.AtivasDasTotais(totais, duracao, tmb.Value);
            alertas.Add(tmb is null
                ? "O print não mostra as calorias ativas: usei as totais (registre um peso para descontar o repouso)."
                : "O print não mostra as calorias ativas: estimei pelas totais menos o repouso do período.");
        }

        var dto = new NovaAtividadeDto
        {
            DataHora = dataHora, Tipo = tipo, Descricao = tipo == TipoAtividade.Outra ? Limitar(r.Descricao, 100) : null,
            DuracaoMin = duracao, KcalAtivas = ativas, KcalTotais = r.KcalTotais, DistanciaM = r.DistanciaM,
            FcMedia = r.FcMedia, Origem = OrigemAtividade.Print
        };
        if (!r.EhResumoDeAtividade)
        {
            alertas.Insert(0, "Não parece o resumo de uma atividade. Confira os campos ou preencha à mão.");
            return new RascunhoAtividadeDto(dto, false, alertas, false, null);
        }
        if (duracao == 0) alertas.Add("Duração não encontrada no print. Preencha.");
        return new RascunhoAtividadeDto(dto, true, alertas, false, null);
    }

    /// <summary>
    /// Grava a atividade confirmada. O mesmo print enviado de novo (mesmo tipo, mesmo minuto) devolve a já gravada.
    /// Sem calorias, estima pela tabela MET com o peso atual.
    /// </summary>
    public async Task<AtividadeDto> CriarAsync(NovaAtividadeDto dto, CancellationToken ct)
    {
        var dataHora = dto.DataHora ?? relogio.Agora;
        if (dataHora > relogio.Agora + ToleranciaFuturo) throw new ValidacaoException("A atividade não pode estar no futuro.");
        if (dto.Tipo == TipoAtividade.Outra && string.IsNullOrWhiteSpace(dto.Descricao))
            throw new ValidacaoException("Diga qual é a atividade.");

        var regras = await perfil.ObterRegrasAsync(ct);
        var minuto = new DateTime(dataHora.Year, dataHora.Month, dataHora.Day, dataHora.Hour, dataHora.Minute, 0, dataHora.Kind);
        var existente = await db.Atividades.FirstOrDefaultAsync(a => a.UsuarioId == usuario.UsuarioId && a.Tipo == dto.Tipo
            && a.DataHora >= minuto && a.DataHora < minuto.AddMinutes(1), ct);
        if (existente is not null) return ParaDto(existente, regras);

        var kcal = dto.KcalAtivas;
        if (kcal is null)
        {
            var peso = await PesoAsync(ct) ?? throw new ValidacaoException("Sem calorias informadas e sem peso registrado para estimar.");
            kcal = GastoAtividades.EstimarKcalAtivas(dto.Tipo, dto.DuracaoMin, peso);
        }

        var a = new Atividade
        {
            UsuarioId = usuario.UsuarioId, DataHora = dataHora, Tipo = dto.Tipo, Descricao = Limitar(dto.Descricao, 100),
            DuracaoMin = dto.DuracaoMin, KcalAtivas = kcal.Value, KcalTotais = dto.KcalTotais, DistanciaM = dto.DistanciaM,
            FcMedia = dto.FcMedia, Origem = dto.Origem
        };
        db.Atividades.Add(a);
        await db.SaveChangesAsync(ct);
        return ParaDto(a, regras);
    }

    public async Task<IReadOnlyList<AtividadeDto>> ListarAsync(DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var regras = await perfil.ObterRegrasAsync(ct);
        var ini = de.ToDateTime(TimeOnly.MinValue);
        var fim = ate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return (await db.Atividades.Where(a => a.UsuarioId == usuario.UsuarioId && a.DataHora >= ini && a.DataHora < fim)
                .OrderByDescending(a => a.DataHora).ToListAsync(ct))
            .Select(a => ParaDto(a, regras)).ToList();
    }

    public async Task RemoverAsync(int id, CancellationToken ct)
    {
        var a = await db.Atividades.FirstOrDefaultAsync(x => x.Id == id && x.UsuarioId == usuario.UsuarioId, ct)
            ?? throw new NaoEncontradoException("Atividade não encontrada.");
        db.Atividades.Remove(a);
        await db.SaveChangesAsync(ct);
    }

    private static AtividadeDto ParaDto(Atividade a, ConfiguracaoRegras regras) =>
        new(a.Id, a.DataHora, a.Tipo, a.Descricao, a.DuracaoMin, a.KcalAtivas, a.KcalTotais, a.DistanciaM, a.FcMedia, a.Origem,
            GastoAtividades.Credito([a.KcalAtivas], regras));

    private async Task<decimal?> TmbAsync(CancellationToken ct)
    {
        try
        {
            var p = await perfil.PerfilCalculoAsync(ct);
            return CalculadoraMetas.Tmb(p.Sexo, p.PesoKg, p.AlturaCm, p.Idade);
        }
        catch (ValidacaoException) { return null; }
    }

    private async Task<decimal?> PesoAsync(CancellationToken ct)
    {
        try { return (await perfil.PerfilCalculoAsync(ct)).PesoKg; }
        catch (ValidacaoException) { return null; }
    }

    private static string? Limitar(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];
}
