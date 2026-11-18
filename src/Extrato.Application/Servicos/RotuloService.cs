using Extrato.Contracts;
using Microsoft.Extensions.Logging;

namespace Extrato.Application.Servicos;

/// <summary>
/// Foto da tabela nutricional → números para o cadastro de alimento por rótulo. Nada é gravado: a pessoa
/// confere os campos e salva pela tela (POST /alimentos). A imagem vive só neste escopo.
/// </summary>
public class RotuloService(IVisionProvider vision, IProcessadorImagem processador, ILogger<RotuloService> logger)
{
    public async Task<RascunhoRotuloDto> AnalisarAsync(Stream imagem, CancellationToken ct)
    {
        await using var preparada = await processador.PrepararAsync(imagem, ct);
        AnaliseRotuloResultado r;
        try
        {
            r = await vision.AnalisarRotuloAsync(preparada, ct);
        }
        catch (VisionIndisponivelException ex)
        {
            logger.LogWarning("IA de visão indisponível para rótulo ({Provedor}): {Motivo}", vision.Nome, ex.Message);
            return new RascunhoRotuloDto(null, 100, 0, 0, 0, 0, null, [], true, ex.Message);
        }

        var alertas = r.Alertas.ToList();
        if (!r.EhTabelaNutricional)
            alertas.Insert(0, "Não achei uma tabela nutricional na foto. Fotografe a tabela de perto, reta e sem reflexo.");

        var porcao = r.PorcaoG ?? 100;
        if (r.PorcaoG is null && r.EhTabelaNutricional)
            alertas.Add("Porção não encontrada no rótulo: considerei 100 g. Confira.");

        var kcal = r.Kcal ?? 0;
        var p = r.ProteinaG ?? 0;
        var c = r.CarboG ?? 0;
        var g = r.GorduraG ?? 0;
        // conferência: 4 kcal/g de proteína e carbo, 9 de gordura (fibra e arredondamentos explicam pouca diferença)
        var calculado = p * 4 + c * 4 + g * 9;
        if (kcal > 0 && calculado > 0 && Math.Abs(calculado - kcal) > Math.Max(15, kcal * 0.25m))
            alertas.Add($"As calorias ({kcal:0} kcal) não batem com os macros (~{calculado:0} kcal). Confira os números.");

        return new RascunhoRotuloDto(r.Nome, porcao, kcal, p, c, g, r.FibraG, alertas, false, null);
    }
}
