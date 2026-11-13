using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;
using Extrato.Infrastructure.Dados;
using Microsoft.EntityFrameworkCore;

namespace Extrato.Application.Tests;

/// <summary>Integridade da TACO extraída do PDF oficial e do seed que a carrega.</summary>
public class TacoTests
{
    private static readonly IReadOnlyList<SemeadorDados.LinhaTaco> Taco = SemeadorDados.LerTaco();

    private static ExtratoDbContext NovoDb() =>
        new(new DbContextOptionsBuilder<ExtratoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void Tabela_completa_tem_593_alimentos_com_numeros_unicos()
    {
        // 597 itens na TACO 4ª ed.; 4 estão "em análise" (*) e ficam de fora
        Taco.Should().HaveCount(593);
        Taco.Select(t => t.Numero).Should().OnlyHaveUniqueItems();
        Taco.Select(t => t.Numero).Should().OnlyContain(n => n >= 1 && n <= 597);
    }

    [Fact]
    public void Nomes_normalizados_sao_unicos() =>
        Taco.Select(t => TextoBusca.Normalizar(t.Nome)).Should().OnlyHaveUniqueItems();

    [Fact]
    public void Valores_sao_coerentes()
    {
        Taco.Should().OnlyContain(t => t.Kcal >= 0 && t.Proteina >= 0 && t.Carbo >= 0 && t.Gordura >= 0);
        Taco.Should().OnlyContain(t => t.Proteina + t.Carbo + t.Gordura <= 100.5m);
        Taco.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Categoria));
    }

    [Theory]
    [InlineData("Arroz, tipo 1, cozido", 128, 2.5, 28.1, 0.2)]
    [InlineData("Feijão, carioca, cozido", 76, 4.8, 13.6, 0.5)]
    [InlineData("Frango, peito, sem pele, grelhado", 159, 32.0, 0, 2.5)]
    [InlineData("Óleo, de soja", 884, 0, 0, 100)]
    [InlineData("Estrogonofe de frango", 157, 17.6, 2.6, 8.0)]
    [InlineData("Cerveja, pilsen", 41, 0.6, 3.3, 0)]
    public void Valores_conferem_com_o_PDF_oficial(string nome, double kcal, double p, double c, double g)
    {
        var t = Taco.Single(x => x.Nome == nome);
        t.Kcal.Should().Be((decimal)kcal);
        t.Proteina.Should().Be((decimal)p);
        t.Carbo.Should().Be((decimal)c);
        t.Gordura.Should().Be((decimal)g);
    }

    [Fact]
    public void Itens_sem_dados_na_TACO_ficam_de_fora() =>
        Taco.Select(t => t.Nome).Should().NotContain(["Leite, de vaca, integral", "Leite, de vaca, desnatado, UHT", "Coco, verde, cru"]);

    [Fact]
    public void Todo_sinonimo_aponta_para_um_alimento_existente()
    {
        var nomes = Taco.Select(t => t.Nome).ToHashSet();
        SemeadorDados.Sinonimos.Keys.Should().OnlyContain(k => nomes.Contains(k));
    }

    [Fact]
    public async Task Seed_e_idempotente()
    {
        await using var db = NovoDb();
        await SemeadorDados.SemearAlimentosAsync(db);
        var total = await db.Alimentos.CountAsync();
        total.Should().Be(593 + 4); // TACO + 4 itens de rótulo

        await SemeadorDados.SemearAlimentosAsync(db);
        (await db.Alimentos.CountAsync()).Should().Be(total);
        (await db.AlimentoSinonimos.CountAsync(s => s.Termo == "arroz")).Should().Be(1);
    }

    [Fact]
    public async Task Banco_antigo_e_atualizado_sem_duplicar_e_sem_perder_ids()
    {
        await using var db = NovoDb();
        // simula a versão anterior do seed (amostra com nomes antigos e valores aproximados)
        var leite = new Alimento { Nome = "Leite, de vaca, integral", NomeNormalizado = TextoBusca.Normalizar("Leite, de vaca, integral"), Fonte = FonteAlimento.Taco, Kcal100g = 61, Proteina100g = 2.9m, Carbo100g = 4.3m, Gordura100g = 3.2m };
        var alcatra = new Alimento { Nome = "Carne, bovina, alcatra, sem gordura, grelhada", NomeNormalizado = TextoBusca.Normalizar("Carne, bovina, alcatra, sem gordura, grelhada"), Fonte = FonteAlimento.Taco, Kcal100g = 241 };
        var arroz = new Alimento { Nome = "Arroz, tipo 1, cozido", NomeNormalizado = TextoBusca.Normalizar("Arroz, tipo 1, cozido"), Fonte = FonteAlimento.Taco, Kcal100g = 999 };
        var meuIogurte = new Alimento { Nome = "Iogurte, natural", NomeNormalizado = TextoBusca.Normalizar("Iogurte, natural"), Fonte = FonteAlimento.Personalizado, Kcal100g = 70 };
        db.Alimentos.AddRange(leite, alcatra, arroz, meuIogurte);
        await db.SaveChangesAsync();
        var ids = (leite.Id, alcatra.Id, arroz.Id);

        await SemeadorDados.SemearAlimentosAsync(db);

        (await db.Alimentos.CountAsync()).Should().Be(593 + 4);
        var l = await db.Alimentos.SingleAsync(a => a.Id == ids.Item1);
        l.Nome.Should().Be("Leite integral UHT (rótulo)");
        l.Fonte.Should().Be(FonteAlimento.Personalizado);
        (await db.Alimentos.SingleAsync(a => a.Id == ids.Item2)).Nome.Should().Be("Carne, bovina, miolo de alcatra, sem gordura, grelhado");
        (await db.Alimentos.SingleAsync(a => a.Id == ids.Item3)).Kcal100g.Should().Be(128, "valor da TACO substitui o antigo");
        (await db.Alimentos.SingleAsync(a => a.Id == meuIogurte.Id)).Kcal100g.Should().Be(70, "cadastro por rótulo do usuário não é sobrescrito");
    }

    [Fact]
    public async Task Busca_encontra_pratos_que_antes_nao_existiam()
    {
        await using var db = NovoDb();
        await SemeadorDados.SemearAlimentosAsync(db);
        var servico = new Application.Servicos.AlimentoService(db);
        (await servico.MatchAsync("feijoada", null, default))!.Alimento.Nome.Should().Be("Feijoada");
        (await servico.MatchAsync("tapioca", null, default))!.Alimento.Nome.Should().StartWith("Tapioca");
        (await servico.MatchAsync("leite", null, default))!.Alimento.Nome.Should().Be("Leite integral UHT (rótulo)");
    }
}
