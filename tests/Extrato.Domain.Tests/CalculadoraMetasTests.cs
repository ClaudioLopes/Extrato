using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;

namespace Extrato.Domain.Tests;

public class CalculadoraMetasTests
{
    private static readonly PerfilCalculo Seed = new(Sexo.Masculino, 72, 170, 31, NivelAtividade.Moderado, Objetivo.GanhoMassa);

    [Fact]
    public void Tmb_homem_segue_Mifflin_St_Jeor() =>
        CalculadoraMetas.Tmb(Sexo.Masculino, 72, 170, 31).Should().Be(1632.5m);

    [Fact]
    public void Tmb_mulher_usa_menos_161() =>
        CalculadoraMetas.Tmb(Sexo.Feminino, 60, 165, 30).Should().Be(10 * 60 + 6.25m * 165 - 5 * 30 - 161);

    [Theory]
    [InlineData(NivelAtividade.Sedentario, 1.2)]
    [InlineData(NivelAtividade.Leve, 1.375)]
    [InlineData(NivelAtividade.Moderado, 1.55)]
    [InlineData(NivelAtividade.Alto, 1.725)]
    [InlineData(NivelAtividade.MuitoAlto, 1.9)]
    public void Fator_de_atividade_por_nivel(NivelAtividade nivel, double esperado) =>
        CalculadoraMetas.FatorAtividade(nivel).Should().Be((decimal)esperado);

    [Fact]
    public void Exemplo_da_documentacao_perfil_seed()
    {
        var r = CalculadoraMetas.Calcular(Seed, new ConfiguracaoRegras());

        r.Tmb.Should().BeApproximately(1633, 1);
        r.Get.Should().BeApproximately(2530, 1);
        r.Kcal.Should().Be(2830);
        r.ProteinaG.Should().Be(144);
        r.GorduraG.Should().BeApproximately(65, 0.5m);
        r.CarboG.Should().BeApproximately(417, 1);
        r.Racional.Should().NotBeEmpty();
    }

    [Fact]
    public void Calorias_dos_macros_fecham_com_a_meta()
    {
        var r = CalculadoraMetas.Calcular(Seed, new ConfiguracaoRegras());
        var kcal = r.ProteinaG * 4 + r.CarboG * 4 + r.GorduraG * 9;
        kcal.Should().BeApproximately(r.Kcal, 1);
    }

    [Theory]
    [InlineData(Objetivo.GanhoMassa, 300)]
    [InlineData(Objetivo.Recomposicao, 75)]
    [InlineData(Objetivo.Definicao, -400)]
    public void Ajuste_padrao_fica_dentro_da_faixa_do_objetivo(Objetivo objetivo, int esperado)
    {
        CalculadoraMetas.AjustePadrao(objetivo).Should().Be(esperado);
        var (min, max) = CalculadoraMetas.FaixaAjuste(objetivo);
        esperado.Should().BeInRange(min, max);
    }

    [Fact]
    public void Ajuste_manual_e_fator_manual_sobrescrevem_padroes()
    {
        var regras = new ConfiguracaoRegras { AjusteKcal = 200, FatorAtividade = 1.725m };
        var r = CalculadoraMetas.Calcular(Seed, regras);
        r.FatorAtividade.Should().Be(1.725m);
        r.Kcal.Should().Be((int)Math.Round(1632.5m * 1.725m + 200, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Gordura_nunca_abaixo_de_20_porcento_das_calorias()
    {
        var regras = new ConfiguracaoRegras { GorduraGPorKg = 0.5m };
        var r = CalculadoraMetas.Calcular(Seed, regras);
        (r.GorduraG * 9).Should().BeGreaterThanOrEqualTo(r.Kcal * 0.2m - 1);
    }

    [Fact]
    public void Trava_calorias_minimas_nao_deixa_meta_abaixo_da_TMB()
    {
        var perfil = Seed with { Nivel = NivelAtividade.Sedentario, Objetivo = Objetivo.Definicao };
        var regras = new ConfiguracaoRegras { AjusteKcal = -1000, Travas = new Travas { DeficitMaxKcal = 1000 } };
        var r = CalculadoraMetas.Calcular(perfil, regras);
        r.Kcal.Should().BeGreaterThanOrEqualTo((int)Math.Ceiling(r.Tmb));
    }

    [Fact]
    public void Trava_de_deficit_limita_o_ajuste()
    {
        var regras = new ConfiguracaoRegras { AjusteKcal = -900 };
        var r = CalculadoraMetas.Calcular(Seed with { Objetivo = Objetivo.Definicao }, regras);
        r.AjusteKcal.Should().Be(-500);
    }

    [Fact]
    public void Trava_de_proteina_maxima()
    {
        var regras = new ConfiguracaoRegras { ProteinaGPorKg = 3.0m };
        var r = CalculadoraMetas.Calcular(Seed, regras);
        r.ProteinaG.Should().Be(2.5m * 72);
    }

    [Fact]
    public void Peso_invalido_lanca_excecao()
    {
        var act = () => CalculadoraMetas.Calcular(Seed with { PesoKg = 0 }, new ConfiguracaoRegras());
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Idade_considera_aniversario()
    {
        var u = new Usuario { DataNascimento = new DateOnly(1995, 6, 15) };
        u.IdadeEm(new DateOnly(2026, 6, 14)).Should().Be(30);
        u.IdadeEm(new DateOnly(2026, 6, 15)).Should().Be(31);
    }
}
