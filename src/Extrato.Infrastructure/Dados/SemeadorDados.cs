using Extrato.Application;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;
using Microsoft.EntityFrameworkCore;

namespace Extrato.Infrastructure.Dados;

/// <summary>
/// Seed para uso pessoal (seção 1) e a TACO completa (4ª ed. revisada e ampliada, NEPA/UNICAMP, 2011),
/// carregada de Dados/taco.csv (embutido na DLL). Valores por 100 g de parte comestível.
/// </summary>
public static class SemeadorDados
{
    public const int UsuarioPadraoId = 1;
    public const string RecursoTaco = "Extrato.taco.csv";

    /// <summary>Sinônimos para o match com a descrição do usuário e com os nomes que a IA devolve.</summary>
    public static readonly Dictionary<string, string[]> Sinonimos = new()
    {
        ["Arroz, tipo 1, cozido"] = ["arroz", "arroz branco", "arroz branco cozido"],
        ["Arroz, integral, cozido"] = ["arroz integral"],
        ["Feijão, carioca, cozido"] = ["feijao", "feijao carioca", "caldo de feijao"],
        ["Feijão, preto, cozido"] = ["feijao preto"],
        ["Frango, peito, sem pele, grelhado"] = ["frango grelhado", "peito de frango", "file de frango", "frango"],
        ["Frango, sobrecoxa, sem pele, assada"] = ["sobrecoxa", "coxa de frango", "frango assado"],
        ["Carne, bovina, contra-filé, sem gordura, grelhado"] = ["bife", "contra file", "carne grelhada", "bife grelhado"],
        ["Carne, bovina, patinho, sem gordura, grelhado"] = ["patinho"],
        ["Carne, bovina, acém, moído, cozido"] = ["carne moida", "patinho moido"],
        ["Carne, bovina, miolo de alcatra, sem gordura, grelhado"] = ["alcatra", "picanha"],
        ["Carne, bovina, fígado, grelhado"] = ["figado", "bife de figado"],
        ["Porco, lombo, assado"] = ["lombo", "carne de porco"],
        ["Lingüiça, porco, grelhada"] = ["linguica"],
        ["Salmão, sem pele, fresco, grelhado"] = ["salmao"],
        ["Atum, conserva em óleo"] = ["atum"],
        ["Ovo, de galinha, inteiro, cozido/10minutos"] = ["ovo", "ovo cozido"],
        ["Ovo, de galinha, inteiro, frito"] = ["ovo frito"],
        ["Pão, trigo, francês"] = ["pao", "pao frances", "pao de sal"],
        ["Pão, trigo, forma, integral"] = ["pao integral", "pao de forma"],
        ["Pão, de queijo, assado"] = ["pao de queijo"],
        ["Iogurte, natural"] = ["iogurte"],
        ["Queijo, minas, frescal"] = ["queijo minas", "queijo branco"],
        ["Queijo, mozarela"] = ["mussarela", "queijo"],
        ["Queijo, requeijão, cremoso"] = ["requeijao"],
        ["Presunto, sem capa de gordura"] = ["presunto"],
        ["Manteiga, com sal"] = ["manteiga"],
        ["Banana, prata, crua"] = ["banana"],
        ["Banana, nanica, crua"] = ["banana nanica"],
        ["Maçã, Fuji, com casca, crua"] = ["maca"],
        ["Morango, cru"] = ["morango"],
        ["Mamão, Papaia, cru"] = ["mamao"],
        ["Laranja, pêra, crua"] = ["laranja"],
        ["Laranja, pêra, suco"] = ["suco de laranja"],
        ["Abacate, cru"] = ["abacate"],
        ["Aveia, flocos, crua"] = ["aveia"],
        ["Batata, inglesa, cozida"] = ["batata", "batata cozida", "pure de batata"],
        ["Batata, inglesa, frita"] = ["batata frita", "fritas"],
        ["Batata, doce, cozida"] = ["batata doce"],
        ["Mandioca, cozida"] = ["mandioca", "aipim", "macaxeira"],
        ["Cuscuz, de milho, cozido com sal"] = ["cuscuz"],
        ["Farinha, de mandioca, torrada"] = ["farofa", "farinha de mandioca"],
        ["Alface, crespa, crua"] = ["alface", "salada"],
        ["Tomate, com semente, cru"] = ["tomate"],
        ["Cenoura, crua"] = ["cenoura"],
        ["Brócolis, cozido"] = ["brocolis"],
        ["Macarrão, trigo, cru"] = ["macarrao", "massa", "espaguete"],
        ["Amendoim, grão, cru"] = ["amendoim"],
        ["Óleo, de soja"] = ["oleo", "oleo de cozinha"],
        ["Azeite, de oliva, extra virgem"] = ["azeite"],
        ["Açúcar, refinado"] = ["acucar"],
        ["Mel, de abelha"] = ["mel"],
        ["Chocolate, ao leite"] = ["chocolate"],
        ["Café, infusão 10%"] = ["cafe"],
        ["Tapioca, com manteiga"] = ["tapioca"],
        ["Feijoada"] = ["feijoada"],
        ["Cerveja, pilsen"] = ["cerveja"],
    };

    /// <summary>
    /// Itens sem dados na TACO (leite UHT aparece "em análise") ou industrializados (seção 2.4):
    /// valores de rótulo típicos, para o usuário ajustar pelo próprio rótulo.
    /// </summary>
    internal static readonly (string Nome, decimal Kcal, decimal P, decimal C, decimal G, string[] Sin)[] Rotulos =
    [
        ("Whey protein (rótulo)", 400, 80, 10, 5, ["whey", "whey protein"]),
        ("Creatina monohidratada (rótulo)", 0, 0, 0, 0, ["creatina"]),
        ("Leite integral UHT (rótulo)", 59, 3.2m, 4.7m, 3.0m, ["leite", "leite integral"]),
        ("Leite desnatado UHT (rótulo)", 34, 3.2m, 4.8m, 0.2m, ["leite desnatado"]),
    ];

    /// <summary>Nomes da versão anterior do seed que mudaram: renomeia a linha (mantém o Id e o histórico).</summary>
    internal static readonly (string Antigo, string Novo, FonteAlimento Fonte)[] Renomes =
    [
        ("Carne, bovina, alcatra, sem gordura, grelhada", "Carne, bovina, miolo de alcatra, sem gordura, grelhado", FonteAlimento.Taco),
        ("Requeijão, cremoso", "Queijo, requeijão, cremoso", FonteAlimento.Taco),
        ("Leite, de vaca, integral", "Leite integral UHT (rótulo)", FonteAlimento.Personalizado),
        ("Leite, de vaca, desnatado, UHT", "Leite desnatado UHT (rótulo)", FonteAlimento.Personalizado),
    ];

    public record LinhaTaco(int Numero, string Nome, string Categoria, decimal Kcal, decimal Proteina, decimal Carbo, decimal Gordura, decimal? Fibra);

    /// <summary>Lê a TACO embutida (nome;kcal;proteina;carboidrato;gordura;fibra;numero_taco;categoria).</summary>
    public static IReadOnlyList<LinhaTaco> LerTaco()
    {
        using var stream = typeof(SemeadorDados).Assembly.GetManifestResourceStream(RecursoTaco)
            ?? throw new InvalidOperationException($"Recurso {RecursoTaco} não encontrado.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        decimal D(string s) => decimal.Parse(s, System.Globalization.NumberStyles.Number, pt);
        var linhas = new List<LinhaTaco>();
        reader.ReadLine(); // cabeçalho
        string? l;
        while ((l = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(l)) continue;
            var c = l.Split(';');
            linhas.Add(new LinhaTaco(int.Parse(c[6]), c[0], c[7], D(c[1]), D(c[2]), D(c[3]), D(c[4]), c[5].Length == 0 ? null : D(c[5])));
        }
        return linhas;
    }

    public static async Task SemearAsync(IAppDbContext db, IRelogio relogio, CancellationToken ct = default)
    {
        await SemearAlimentosAsync(db, ct);

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
                Itens = [Item("Leite integral UHT (rótulo)", 300), Item("Whey protein (rótulo)", 30), Item("Creatina monohidratada (rótulo)", 5), Item("Morango, cru", 100)]
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

    /// <summary>
    /// Idempotente: insere ou atualiza as linhas da TACO pelo nome normalizado, sem tocar em
    /// alimentos de rótulo do usuário, renomeia nomes antigos do seed e completa sinônimos.
    /// </summary>
    public static async Task SemearAlimentosAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var existentes = await db.Alimentos.Include(a => a.Sinonimos).ToListAsync(ct);
        var porNome = existentes.ToDictionary(a => a.NomeNormalizado);

        foreach (var (antigo, novo, fonte) in Renomes)
        {
            var normNovo = TextoBusca.Normalizar(novo);
            if (porNome.TryGetValue(TextoBusca.Normalizar(antigo), out var a) && !porNome.ContainsKey(normNovo))
            {
                porNome.Remove(a.NomeNormalizado);
                a.Nome = novo; a.NomeNormalizado = normNovo; a.Fonte = fonte;
                porNome[normNovo] = a;
            }
        }

        foreach (var t in LerTaco())
        {
            var norm = TextoBusca.Normalizar(t.Nome);
            if (porNome.TryGetValue(norm, out var a))
            {
                if (a.Fonte != FonteAlimento.Taco) continue; // não sobrescreve cadastro por rótulo
                a.Nome = t.Nome; a.Kcal100g = t.Kcal; a.Proteina100g = t.Proteina; a.Carbo100g = t.Carbo;
                a.Gordura100g = t.Gordura; a.Fibra100g = t.Fibra;
            }
            else
            {
                a = NovoAlimento(t.Nome, FonteAlimento.Taco, t.Kcal, t.Proteina, t.Carbo, t.Gordura, t.Fibra, []);
                db.Alimentos.Add(a);
                porNome[norm] = a;
            }
        }

        foreach (var r in Rotulos)
        {
            var norm = TextoBusca.Normalizar(r.Nome);
            if (!porNome.ContainsKey(norm))
            {
                var a = NovoAlimento(r.Nome, FonteAlimento.Personalizado, r.Kcal, r.P, r.C, r.G, null, []);
                db.Alimentos.Add(a);
                porNome[norm] = a;
            }
        }

        foreach (var (nome, termos) in Sinonimos.Concat(Rotulos.Select(r => KeyValuePair.Create(r.Nome, r.Sin))))
        {
            if (!porNome.TryGetValue(TextoBusca.Normalizar(nome), out var a)) continue;
            foreach (var termo in termos.Select(TextoBusca.Normalizar).Distinct())
                if (a.Sinonimos.All(s => s.Termo != termo))
                    a.Sinonimos.Add(new AlimentoSinonimo { Termo = termo });
        }

        await db.SaveChangesAsync(ct);
    }

    private static Alimento NovoAlimento(string nome, FonteAlimento fonte, decimal kcal, decimal p, decimal c, decimal g, decimal? f, string[] sinonimos) => new()
    {
        Nome = nome, NomeNormalizado = TextoBusca.Normalizar(nome), Fonte = fonte,
        Kcal100g = kcal, Proteina100g = p, Carbo100g = c, Gordura100g = g, Fibra100g = f,
        Sinonimos = sinonimos.Select(TextoBusca.Normalizar).Distinct().Select(s => new AlimentoSinonimo { Termo = s }).ToList()
    };
}
