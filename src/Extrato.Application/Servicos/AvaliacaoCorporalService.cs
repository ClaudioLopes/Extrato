using Extrato.Contracts;
using Microsoft.Extensions.Logging;

namespace Extrato.Application.Servicos;

/// <summary>
/// Fase 2 / seção 5.3: avaliação corporal por foto. Só roda com provedor que aceita dado sensível
/// (pago ou local); a imagem vive só nesta requisição e nada dela é gravado. O resultado é um
/// rascunho: o usuário decide se salva a faixa e as observações junto com as medidas.
/// O cálculo das metas continua usando peso e medidas, nunca a foto.
/// </summary>
public class AvaliacaoCorporalService(IVisionProvider vision, IProcessadorImagem processador, PerfilService perfil, ILogger<AvaliacaoCorporalService> logger)
{
    public AvaliacaoCorporalDto Situacao() => vision.AceitaDadosSensiveis
        ? new(true, null, null, null, null, [], false, null, vision.Nome)
        : Bloqueado();

    public async Task<AvaliacaoCorporalDto> AnalisarAsync(Stream imagem, string? descricao, CancellationToken ct)
    {
        // checa ANTES de ler/enviar a imagem: com provedor grátis ela nunca sai do servidor
        if (!vision.AceitaDadosSensiveis) return Bloqueado();

        var usuario = await perfil.ObterUsuarioAsync(ct);
        await using var preparada = await processador.PrepararAsync(imagem, ct);
        AnaliseCorpoResultado r;
        try
        {
            r = await vision.AnalisarCorpoAsync(preparada, descricao, usuario.Sexo, ct);
        }
        catch (VisionIndisponivelException ex)
        {
            logger.LogWarning("Avaliação corporal indisponível: {Motivo}", ex.Message);
            return new AvaliacaoCorporalDto(true, null, null, null, null, [], true, ex.Message, vision.Nome);
        }

        var (min, max) = ValidarFaixa(r.GorduraMinPercent, r.GorduraMaxPercent);
        var alertas = r.Alertas.ToList();
        if (r.GorduraMinPercent != null && min is null)
            alertas.Add("A faixa estimada veio fora do plausível e foi descartada.");
        alertas.Add("Estimativa visual, só para acompanhar tendência. As metas usam peso e medidas.");
        return new AvaliacaoCorporalDto(true, null, min, max, r.Observacoes, alertas, false, null, vision.Nome);
    }

    internal static (decimal? Min, decimal? Max) ValidarFaixa(decimal? min, decimal? max)
    {
        if (min is null || max is null) return (null, null);
        var (a, b) = min <= max ? (min.Value, max.Value) : (max.Value, min.Value);
        if (a < 3 || b > 60 || b - a > 15) return (null, null);
        return (Math.Round(a, 1), Math.Round(b, 1));
    }

    private AvaliacaoCorporalDto Bloqueado() => new(false,
        $"O provedor atual ({vision.Nome}) é gratuito e pode usar as imagens enviadas. Foto do corpo só com o Claude (API paga) ou no modo demonstração. Use as medidas.",
        null, null, null, [], false, null, vision.Nome);
}
