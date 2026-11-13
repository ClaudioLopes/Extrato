using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;
using Microsoft.EntityFrameworkCore;

namespace Extrato.Application.Servicos;

/// <summary>
/// Fase 2: cardápio sugerido para as refeições que faltam, respeitando alimentos evitados
/// e a rotina (o que você costuma comer em cada refeição pesa mais na escolha).
/// </summary>
public class CardapioService(IAppDbContext db, IUsuarioAtual usuario, IRelogio relogio, PainelService painel)
{
    public const int DiasHistorico = 60;

    // (nome TACO/rótulo, papel forçado ou null = classificar pelos macros, porção do complemento)
    private static readonly Dictionary<TipoRefeicao, (string Nome, decimal Porcao)[]> Base = new()
    {
        [TipoRefeicao.CafeDaManha] =
        [
            ("Ovo, de galinha, inteiro, cozido/10minutos", 100), ("Queijo, minas, frescal", 30), ("Iogurte, natural", 170),
            ("Whey protein (rótulo)", 30), ("Pão, trigo, francês", 50), ("Pão, trigo, forma, integral", 50),
            ("Aveia, flocos, crua", 30), ("Banana, prata, crua", 90), ("Cuscuz, de milho, cozido com sal", 150),
            ("Tapioca, com manteiga", 100), ("Mamão, Papaia, cru", 150), ("Café, infusão 10%", 100),
        ],
        [TipoRefeicao.Almoco] =
        [
            ("Frango, peito, sem pele, grelhado", 150), ("Carne, bovina, patinho, sem gordura, grelhado", 150),
            ("Carne, bovina, contra-filé, sem gordura, grelhado", 150), ("Salmão, sem pele, fresco, grelhado", 150),
            ("Porco, lombo, assado", 150), ("Ovo, de galinha, inteiro, cozido/10minutos", 100),
            ("Arroz, tipo 1, cozido", 150), ("Arroz, integral, cozido", 150), ("Batata, inglesa, cozida", 150),
            ("Batata, doce, cozida", 150), ("Mandioca, cozida", 150),
            ("Feijão, carioca, cozido", 100), ("Alface, crespa, crua", 30), ("Tomate, com semente, cru", 50),
        ],
        [TipoRefeicao.Tarde] =
        [
            ("Whey protein (rótulo)", 30), ("Iogurte, natural", 170), ("Queijo, minas, frescal", 30),
            ("Ovo, de galinha, inteiro, cozido/10minutos", 100), ("Banana, prata, crua", 90), ("Aveia, flocos, crua", 30),
            ("Pão, trigo, francês", 50), ("Leite integral UHT (rótulo)", 250), ("Morango, cru", 100), ("Amendoim, grão, cru", 15),
        ],
    };

    public async Task<CardapioDto> SugerirAsync(TipoRefeicao tipo, DateOnly? data, CancellationToken ct)
    {
        var dia = data ?? relogio.Hoje;
        var p = await painel.DiaAsync(dia, ct);
        var alvoDto = p.Refeicoes.Single(r => r.Tipo == tipo).Alvo;
        var alvo = new Macros(alvoDto.Kcal, alvoDto.ProteinaG, alvoDto.CarboG, alvoDto.GorduraG);

        var candidatos = await CandidatosAsync(tipo, ct);
        var opcoes = MontadorCardapio.Montar(alvo, candidatos);
        return new CardapioDto(tipo, alvoDto, opcoes.Select(o => new OpcaoCardapioDto(
            o.Itens.Select(i => new ItemCardapioDto(i.AlimentoId, i.Nome, i.Gramas, MacrosDto.De(i.Macros))).ToList(),
            MacrosDto.De(o.Total),
            MacrosDto.De(o.Total - alvo))).ToList());
    }

    internal async Task<List<CandidatoCardapio>> CandidatosAsync(TipoRefeicao tipo, CancellationToken ct)
    {
        var baseTipo = Base[tipo == TipoRefeicao.Noite ? TipoRefeicao.Almoco : tipo];
        var porcoes = baseTipo.ToDictionary(b => TextoBusca.Normalizar(b.Nome), b => b.Porcao);

        // rotina: frequência de cada alimento nessa refeição nos últimos 60 dias
        var desde = relogio.Agora.AddDays(-DiasHistorico);
        var historico = await db.Refeicoes
            .Where(r => r.UsuarioId == usuario.UsuarioId && r.Tipo == tipo && r.DataHora >= desde)
            .SelectMany(r => r.Itens).GroupBy(i => i.AlimentoId)
            .Select(g => new { AlimentoId = g.Key, Vezes = g.Count(), Gramas = g.Average(i => i.Gramas) })
            .ToListAsync(ct);
        var maxVezes = historico.Count == 0 ? 1 : historico.Max(h => h.Vezes);

        var normalizados = porcoes.Keys.ToList();
        var ids = historico.Select(h => h.AlimentoId).ToList();
        var alimentos = await db.Alimentos.AsNoTracking()
            .Where(a => normalizados.Contains(a.NomeNormalizado) || ids.Contains(a.Id)).ToListAsync(ct);

        var evitados = await db.AlimentosEvitados.Where(e => e.UsuarioId == usuario.UsuarioId).ToListAsync(ct);

        var lista = new List<CandidatoCardapio>();
        foreach (var a in alimentos)
        {
            if (RefeicaoService.EhEvitado(evitados, a.Nome, a)) continue;
            var por100 = new Macros(a.Kcal100g, a.Proteina100g, a.Carbo100g, a.Gordura100g);
            if (por100.Kcal <= 0 && a.Proteina100g <= 0) continue;
            var papel = MontadorCardapio.Classificar(por100);
            var h = historico.FirstOrDefault(x => x.AlimentoId == a.Id);
            var pref = h is null ? 0.2m : 0.2m + 0.8m * h.Vezes / maxVezes;
            var porcao = h is not null ? Math.Round(h.Gramas / 5m) * 5m
                : porcoes.TryGetValue(a.NomeNormalizado, out var pc) ? pc : 100m;
            var (min, max) = papel switch
            {
                PapelAlimento.Proteina => (Math.Min(30m, porcao), Math.Max(250m, porcao)),
                PapelAlimento.Carboidrato => (Math.Min(30m, porcao), Math.Max(300m, porcao)),
                _ => (porcao, porcao),
            };
            lista.Add(new CandidatoCardapio(a.Id, a.Nome, por100, papel, min, max, Math.Max(5, porcao), pref));
        }
        return lista;
    }
}
