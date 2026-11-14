using System.Globalization;
using Extrato.Application;

namespace Extrato.Infrastructure.Visao;

/// <summary>
/// Leitura do print do resumo de uma atividade, comum ao Claude e ao Gemini: prompt, tipos aceitos e
/// conversão da resposta. Campos que não aparecem no print vêm como 0 ou "" (o schema não usa null).
/// </summary>
internal record AtividadeJson(
    bool EhResumoDeAtividade,
    string? Tipo,
    string? Descricao,
    string? DataHora,
    int DuracaoSegundos,
    int KcalAtivas,
    int KcalTotais,
    int DistanciaM,
    int FcMedia,
    List<string>? Alertas)
{
    public static readonly string[] Tipos = ["Natacao", "Volei", "Corrida", "Caminhada", "Ciclismo", "Futebol", "Outra"];

    public const string Prompt =
        "Você lê prints da tela de resumo de UMA atividade física em apps de pulseira/relógio (Huawei Health, Zepp, " +
        "Samsung Health, Garmin, Apple Fitness…), em português ou inglês. Copie os números exatamente como aparecem, " +
        "sem estimar nem calcular. Campos: tipo da atividade (Natacao, Volei, Corrida, Caminhada, Ciclismo, Futebol ou " +
        "Outra; em Outra, ponha o nome em descricao), dataHora do início no formato yyyy-MM-ddTHH:mm (sem ano no print, " +
        "use o ano informado), duracaoSegundos (00:50:47 = 3047), kcalAtivas (\"calorias ativas\", \"active calories\", " +
        "\"calorias de exercício\"), kcalTotais (\"calorias totais\", \"total calories\"; se só houver um número de calorias " +
        "sem dizer qual, ponha em kcalTotais), distanciaM em metros (1,3 km = 1300) e fcMedia (frequência cardíaca média em bpm). " +
        "O que não aparecer: 0 nos números e \"\" nos textos. Se a imagem não for o resumo de uma atividade, " +
        "marque ehResumoDeAtividade como false. Use alertas para o que ficou ilegível ou ambíguo. Responda somente no JSON do schema.";

    public static string Texto(DateTime agora) => $"Hoje é {agora:yyyy-MM-dd}; use o ano {agora:yyyy} se o print não mostrar o ano.";

    public AnaliseAtividadeResultado ParaResultado() => new(
        EhResumoDeAtividade,
        Vazio(Tipo),
        Vazio(Descricao),
        DateTime.TryParseExact(DataHora?.Trim(), ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
        Positivo(DuracaoSegundos),
        Positivo(KcalAtivas),
        Positivo(KcalTotais),
        Positivo(DistanciaM),
        Positivo(FcMedia),
        Alertas ?? []);

    private static int? Positivo(int v) => v > 0 ? v : null;
    private static string? Vazio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
