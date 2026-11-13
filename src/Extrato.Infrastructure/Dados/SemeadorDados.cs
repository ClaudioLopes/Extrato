using Extrato.Application;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;
using Microsoft.EntityFrameworkCore;

namespace Extrato.Infrastructure.Dados;

/// <summary>
/// Seed para uso pessoal (seção 1) e uma amostra da TACO (4ª ed., valores por 100 g).
/// A tabela completa pode ser importada via POST /api/alimentos/importar (CSV).
/// </summary>
public static class SemeadorDados
{
    public const int UsuarioPadraoId = 1;

    // nome; kcal; proteína; carboidrato; gordura; fibra; sinônimos
    private static readonly (string Nome, decimal Kcal, decimal P, decimal C, decimal G, decimal? F, string[] Sin)[] Taco =
    [
        ("Arroz, tipo 1, cozido", 128, 2.5m, 28.1m, 0.2m, 1.6m, ["arroz", "arroz branco", "arroz branco cozido"]),
        ("Arroz, integral, cozido", 124, 2.6m, 25.8m, 1.0m, 2.7m, ["arroz integral"]),
        ("Feijão, carioca, cozido", 76, 4.8m, 13.6m, 0.5m, 8.5m, ["feijao", "feijao carioca", "caldo de feijao"]),
        ("Feijão, preto, cozido", 77, 4.5m, 14.0m, 0.5m, 8.4m, ["feijao preto"]),
        ("Frango, peito, sem pele, grelhado", 159, 32.0m, 0m, 2.5m, null, ["frango grelhado", "peito de frango", "file de frango", "frango"]),
        ("Frango, sobrecoxa, sem pele, assada", 233, 29.2m, 0m, 12.0m, null, ["sobrecoxa", "coxa de frango", "frango assado"]),
        ("Carne, bovina, contra-filé, sem gordura, grelhado", 194, 35.9m, 0m, 4.5m, null, ["bife", "contra file", "carne grelhada", "bife grelhado"]),
        ("Carne, bovina, patinho, sem gordura, grelhado", 219, 35.9m, 0m, 7.3m, null, ["patinho"]),
        ("Carne, bovina, acém, moído, cozido", 212, 26.7m, 0m, 10.9m, null, ["carne moida", "patinho moido"]),
        ("Carne, bovina, alcatra, sem gordura, grelhada", 241, 31.9m, 0m, 11.6m, null, ["alcatra", "picanha"]),
        ("Carne, bovina, fígado, grelhado", 225, 29.9m, 4.2m, 9.0m, null, ["figado", "bife de figado"]),
        ("Porco, lombo, assado", 210, 35.7m, 0m, 6.4m, null, ["lombo", "carne de porco"]),
        ("Lingüiça, porco, grelhada", 296, 23.2m, 0m, 21.9m, null, ["linguica"]),
        ("Salmão, sem pele, fresco, grelhado", 229, 23.9m, 0m, 14.0m, null, ["salmao"]),
        ("Atum, conserva em óleo", 166, 26.2m, 0m, 6.0m, null, ["atum"]),
        ("Ovo, de galinha, inteiro, cozido/10minutos", 146, 13.3m, 0.6m, 9.5m, null, ["ovo", "ovo cozido"]),
        ("Ovo, de galinha, inteiro, frito", 240, 15.6m, 1.2m, 18.6m, null, ["ovo frito"]),
        ("Pão, trigo, francês", 300, 8.0m, 58.6m, 3.1m, 2.3m, ["pao", "pao frances", "pao de sal"]),
        ("Pão, trigo, forma, integral", 253, 9.4m, 49.9m, 3.7m, 6.9m, ["pao integral", "pao de forma"]),
        ("Pão, de queijo, assado", 363, 5.1m, 34.2m, 24.6m, 0.6m, ["pao de queijo"]),
        ("Leite, de vaca, integral", 61, 2.9m, 4.3m, 3.2m, null, ["leite", "leite integral"]),
        ("Leite, de vaca, desnatado, UHT", 31, 2.9m, 4.3m, 0.3m, null, ["leite desnatado"]),
        ("Iogurte, natural", 51, 4.1m, 1.9m, 3.0m, null, ["iogurte"]),
        ("Queijo, minas, frescal", 264, 17.4m, 3.2m, 20.2m, null, ["queijo minas", "queijo branco"]),
        ("Queijo, mozarela", 330, 22.6m, 3.0m, 25.2m, null, ["mussarela", "queijo"]),
        ("Requeijão, cremoso", 257, 9.6m, 2.4m, 23.4m, null, ["requeijao"]),
        ("Presunto, sem capa de gordura", 94, 14.3m, 2.1m, 2.7m, null, ["presunto"]),
        ("Manteiga, com sal", 726, 0.4m, 0.1m, 82.4m, null, ["manteiga"]),
        ("Banana, prata, crua", 98, 1.3m, 26.0m, 0.1m, 2.0m, ["banana"]),
        ("Banana, nanica, crua", 92, 1.4m, 23.8m, 0.1m, 1.9m, ["banana nanica"]),
        ("Maçã, Fuji, com casca, crua", 56, 0.3m, 15.2m, 0m, 1.3m, ["maca"]),
        ("Morango, cru", 30, 0.9m, 6.8m, 0.3m, 1.7m, ["morango"]),
        ("Mamão, Papaia, cru", 40, 0.5m, 10.4m, 0.1m, 1.0m, ["mamao"]),
        ("Laranja, pêra, crua", 37, 1.0m, 8.9m, 0.1m, 0.8m, ["laranja"]),
        ("Laranja, pêra, suco", 33, 0.7m, 7.6m, 0.1m, null, ["suco de laranja"]),
        ("Abacate, cru", 96, 1.2m, 6.0m, 8.4m, 6.3m, ["abacate"]),
        ("Aveia, flocos, crua", 394, 13.9m, 66.6m, 8.5m, 9.1m, ["aveia"]),
        ("Batata, inglesa, cozida", 52, 1.2m, 11.9m, 0m, 1.3m, ["batata", "batata cozida", "pure de batata"]),
        ("Batata, inglesa, frita", 267, 5.0m, 35.6m, 13.1m, 8.1m, ["batata frita", "fritas"]),
        ("Batata, doce, cozida", 77, 0.6m, 18.4m, 0.1m, 2.2m, ["batata doce"]),
        ("Mandioca, cozida", 125, 0.6m, 30.1m, 0.3m, 1.6m, ["mandioca", "aipim", "macaxeira"]),
        ("Cuscuz, de milho, cozido com sal", 113, 2.2m, 25.3m, 0.7m, 2.1m, ["cuscuz"]),
        ("Farinha, de mandioca, torrada", 365, 1.2m, 89.2m, 0.3m, 6.5m, ["farofa", "farinha de mandioca"]),
        ("Alface, crespa, crua", 11, 1.3m, 1.7m, 0.2m, 1.8m, ["alface", "salada"]),
        ("Tomate, com semente, cru", 15, 1.1m, 3.1m, 0.2m, 1.2m, ["tomate"]),
        ("Cenoura, crua", 34, 1.3m, 7.7m, 0.2m, 3.2m, ["cenoura"]),
        ("Brócolis, cozido", 25, 2.1m, 4.4m, 0.5m, 3.4m, ["brocolis"]),
        ("Macarrão, trigo, cru", 371, 10.0m, 77.9m, 1.3m, 2.9m, ["macarrao", "massa", "espaguete"]),
        ("Amendoim, grão, cru", 544, 27.2m, 20.3m, 43.9m, 8.0m, ["amendoim"]),
        ("Óleo, de soja", 884, 0m, 0m, 100m, null, ["oleo", "oleo de cozinha"]),
        ("Azeite, de oliva, extra virgem", 884, 0m, 0m, 100m, null, ["azeite"]),
        ("Açúcar, refinado", 387, 0.3m, 99.5m, 0m, null, ["acucar"]),
        ("Mel, de abelha", 309, 0m, 84.0m, 0m, null, ["mel"]),
        ("Chocolate, ao leite", 540, 7.2m, 59.6m, 30.3m, 2.2m, ["chocolate"]),
        ("Café, infusão 10%", 9, 0.7m, 1.5m, 0.1m, null, ["cafe"]),
    ];

    public static async Task SemearAsync(IAppDbContext db, IRelogio relogio, CancellationToken ct = default)
    {
        if (!await db.Alimentos.AnyAsync(ct))
        {
            foreach (var t in Taco)
                db.Alimentos.Add(NovoAlimento(t.Nome, FonteAlimento.Taco, t.Kcal, t.P, t.C, t.G, t.F, t.Sin));

            // Industrializados não estão na TACO: cadastro por rótulo (seção 2.4)
            db.Alimentos.Add(NovoAlimento("Whey protein (rótulo)", FonteAlimento.Personalizado, 400, 80, 10, 5, null, ["whey", "whey protein"]));
            db.Alimentos.Add(NovoAlimento("Creatina monohidratada (rótulo)", FonteAlimento.Personalizado, 0, 0, 0, 0, null, ["creatina"]));
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Usuarios.AnyAsync(ct))
        {
            var hoje = relogio.Hoje;
            var usuario = new Usuario
            {
                Id = UsuarioPadraoId,
                Nome = "Eu",
                Sexo = Sexo.Masculino,
                DataNascimento = hoje.AddYears(-31).AddDays(-30),
                AlturaCm = 170,
                NivelAtividade = NivelAtividade.Moderado,
                Objetivo = Objetivo.GanhoMassa,
                AlimentosEvitados = new[] { "massa", "macarrao", "figado" }.Select(t => new AlimentoEvitado { UsuarioId = UsuarioPadraoId, Termo = t }).ToList()
            };
            var regras = ConfiguracaoRegras.PadraoPara(UsuarioPadraoId);
            usuario.Regras = regras;
            db.Usuarios.Add(usuario);
            db.Pesos.Add(new RegistroPeso { UsuarioId = UsuarioPadraoId, Data = hoje, PesoKg = 72 });

            var r = CalculadoraMetas.Calcular(new PerfilCalculo(usuario.Sexo, 72, usuario.AlturaCm, usuario.IdadeEm(hoje), usuario.NivelAtividade, usuario.Objetivo), regras);
            db.Metas.Add(new MetaDiaria
            {
                UsuarioId = UsuarioPadraoId, VigenteDesde = hoje, Kcal = r.Kcal, ProteinaG = r.ProteinaG,
                CarboG = r.CarboG, GorduraG = r.GorduraG, Origem = OrigemMeta.Inicial
            });
            await db.SaveChangesAsync(ct);

            var porNome = await db.Alimentos.ToDictionaryAsync(a => a.Nome, ct);
            PorcaoFavoritaItem Item(string nome, decimal g) => new() { AlimentoId = porNome[nome].Id, Gramas = g };
            db.Favoritos.Add(new PorcaoFavorita
            {
                UsuarioId = UsuarioPadraoId, Nome = "Vitamina (leite, whey, creatina, morango)", TipoSugerido = TipoRefeicao.Tarde,
                Itens = [Item("Leite, de vaca, integral", 300), Item("Whey protein (rótulo)", 30), Item("Creatina monohidratada (rótulo)", 5), Item("Morango, cru", 100)]
            });
            db.Favoritos.Add(new PorcaoFavorita
            {
                UsuarioId = UsuarioPadraoId, Nome = "Pão com ovos", TipoSugerido = TipoRefeicao.CafeDaManha,
                Itens = [Item("Pão, trigo, francês", 50), Item("Ovo, de galinha, inteiro, cozido/10minutos", 100), Item("Queijo, minas, frescal", 30)]
            });
            db.Favoritos.Add(new PorcaoFavorita
            {
                UsuarioId = UsuarioPadraoId, Nome = "Prato feito com frango", TipoSugerido = TipoRefeicao.Almoco,
                Itens = [Item("Arroz, tipo 1, cozido", 200), Item("Feijão, carioca, cozido", 140), Item("Frango, peito, sem pele, grelhado", 150), Item("Alface, crespa, crua", 30), Item("Tomate, com semente, cru", 50)]
            });
            db.Favoritos.Add(new PorcaoFavorita
            {
                UsuarioId = UsuarioPadraoId, Nome = "Iogurte com aveia e banana", TipoSugerido = TipoRefeicao.Noite,
                Itens = [Item("Iogurte, natural", 170), Item("Aveia, flocos, crua", 30), Item("Banana, prata, crua", 80), Item("Mel, de abelha", 15)]
            });

            db.Academias.Add(new Academia { Nome = "Academia principal" });
            await db.SaveChangesAsync(ct);
        }
    }

    private static Alimento NovoAlimento(string nome, FonteAlimento fonte, decimal kcal, decimal p, decimal c, decimal g, decimal? f, string[] sinonimos) => new()
    {
        Nome = nome, NomeNormalizado = TextoBusca.Normalizar(nome), Fonte = fonte,
        Kcal100g = kcal, Proteina100g = p, Carbo100g = c, Gordura100g = g, Fibra100g = f,
        Sinonimos = sinonimos.Select(TextoBusca.Normalizar).Distinct().Select(s => new AlimentoSinonimo { Termo = s }).ToList()
    };
}
