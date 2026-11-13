using System.Text.RegularExpressions;
using Extrato.Application;
using Extrato.Domain;

namespace Extrato.Infrastructure.Visao;

/// <summary>
/// Provedor offline para testes e para a demo do portfólio (roda sem chave de API).
/// Usa a descrição do usuário quando houver ("150 g de frango, 2 conchas de arroz");
/// senão devolve um prato típico por tipo de refeição.
/// </summary>
public partial class FakeVisionProvider : IVisionProvider
{
    public string Nome => "Fake (demo offline)";

    // demo offline: nada sai da máquina
    public bool AceitaDadosSensiveis => true;

    public Task<AnaliseCorpoResultado> AnalisarCorpoAsync(Stream imagem, string? descricao, Sexo sexo, CancellationToken ct) =>
        Task.FromResult(new AnaliseCorpoResultado(
            sexo == Sexo.Masculino ? 14 : 22, sexo == Sexo.Masculino ? 18 : 27,
            "Estimativa simulada (FakeVisionProvider). Configure a chave do Claude para uma análise real.",
            ["Análise simulada: não use para decisões."]));

    public Task<AnalisePratoResultado> AnalisarPratoAsync(Stream imagem, string? descricao, TipoRefeicao tipo, CancellationToken ct)
    {
        var itens = string.IsNullOrWhiteSpace(descricao) ? [] : DaDescricao(descricao);
        if (itens.Count == 0) itens = PratoTipico(tipo);
        return Task.FromResult(new AnalisePratoResultado(itens, ["Análise simulada (FakeVisionProvider): configure Gemini:ApiKey para usar IA real."]));
    }

    public Task<AnaliseAparelhoResultado> AnalisarAparelhoAsync(Stream imagem, string? nome, CancellationToken ct) =>
        Task.FromResult(new AnaliseAparelhoResultado(
            string.IsNullOrWhiteSpace(nome) ? "Leg press 45°" : nome, "Quadríceps", "Anilha", false,
            ["Análise simulada (FakeVisionProvider)."]));

    private static readonly Dictionary<string, decimal> Medidas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["concha"] = 70, ["conchas"] = 70, ["colher"] = 25, ["colheres"] = 25, ["escumadeira"] = 90,
        ["fatia"] = 25, ["fatias"] = 25, ["unidade"] = 50, ["unidades"] = 50, ["copo"] = 250, ["copos"] = 250,
    };

    private static List<ItemIdentificado> DaDescricao(string descricao)
    {
        var itens = new List<ItemIdentificado>();
        foreach (var parte in Separador().Split(descricao))
        {
            var p = parte.Trim();
            if (p.Length == 0) continue;
            var g = Gramas().Match(p);
            if (g.Success)
            {
                var nome = Gramas().Replace(p, "").Replace(" de ", " ").Trim(' ', '~');
                itens.Add(new(nome.Length > 0 ? nome : p, null, Math.Clamp(decimal.Parse(g.Groups[1].Value), 1, 1500), 0.9m, "quantidade da descrição"));
                continue;
            }
            var q = Quantidade().Match(p);
            if (q.Success && Medidas.TryGetValue(q.Groups[2].Value, out var porMedida))
            {
                var nome = p[(q.Index + q.Length)..].Trim();
                if (nome.StartsWith("de ")) nome = nome[3..];
                itens.Add(new(nome, null, Math.Clamp(int.Parse(q.Groups[1].Value) * porMedida, 1, 1500), 0.75m, $"{q.Value} segundo a descrição"));
                continue;
            }
            itens.Add(new(p, null, 100, 0.4m, "quantidade não informada; estimativa padrão"));
        }
        return itens;
    }

    private static List<ItemIdentificado> PratoTipico(TipoRefeicao tipo) => tipo switch
    {
        TipoRefeicao.CafeDaManha => [new("pão francês", null, 50, 0.8m, null), new("ovo cozido", "cozido", 100, 0.7m, null), new("café", null, 100, 0.6m, null)],
        TipoRefeicao.Tarde => [new("banana", null, 90, 0.8m, null), new("iogurte natural", null, 170, 0.6m, null)],
        _ =>
        [
            new("arroz branco cozido", "cozido", 150, 0.8m, null),
            new("feijão carioca", "cozido", 120, 0.75m, null),
            new("peito de frango grelhado", "grelhado", 130, 0.7m, null),
            new("alface", "crua", 20, 0.6m, null),
            new("óleo de soja", "usado no preparo", 8, 0.3m, "estimado pelo brilho do arroz"),
        ]
    };

    [GeneratedRegex(@",|\+|\be\b", RegexOptions.IgnoreCase)] private static partial Regex Separador();
    [GeneratedRegex(@"~?\s*(\d+)\s*g\b", RegexOptions.IgnoreCase)] private static partial Regex Gramas();
    [GeneratedRegex(@"^(\d+)\s+(\w+)", RegexOptions.IgnoreCase)] private static partial Regex Quantidade();
}
