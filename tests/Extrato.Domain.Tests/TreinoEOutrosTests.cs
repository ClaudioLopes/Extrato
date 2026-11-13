using Extrato.Domain;
using Extrato.Domain.Regras;

namespace Extrato.Domain.Tests;

public class MetricasTreinoTests
{
    private static SessaoAparelho S(int dia, params (decimal carga, int reps)[] series) =>
        new(new DateTime(2026, 11, dia), series.Select(s => new SerieRegistrada(s.carga, s.reps)).ToList());

    [Theory]
    [InlineData(100, 1, 100)]
    [InlineData(100, 10, 133.3)]
    [InlineData(60, 12, 84)]
    [InlineData(0, 10, 0)]
    [InlineData(50, 0, 0)]
    public void Epley(decimal carga, int reps, decimal esperado) => MetricasTreino.Epley(carga, reps).Should().Be(esperado);

    [Fact]
    public void Volume_soma_carga_vezes_reps() =>
        MetricasTreino.Volume([new(50, 10), new(50, 8), new(40, 12)]).Should().Be(500 + 400 + 480);

    [Fact]
    public void Recorde_pessoal_e_o_maior_1rm()
    {
        var sessoes = new[] { S(1, (50, 10)), S(3, (60, 5)), S(5, (55, 10)) };
        MetricasTreino.RecordePessoal(sessoes).Should().Be(MetricasTreino.Epley(55, 10));
    }

    [Fact]
    public void Estagnado_com_tres_sessoes_sem_progresso()
    {
        var sessoes = new[] { S(1, (50, 10)), S(3, (50, 10)), S(5, (50, 9)), S(7, (50, 10)) };
        MetricasTreino.Estagnado(sessoes).Should().BeTrue();
    }

    [Fact]
    public void Nao_estagnado_se_reps_subiram_na_mesma_carga()
    {
        var sessoes = new[] { S(1, (50, 10)), S(3, (50, 10)), S(5, (50, 11)), S(7, (50, 10)) };
        MetricasTreino.Estagnado(sessoes).Should().BeFalse();
    }

    [Fact]
    public void Nao_estagnado_com_poucas_sessoes() =>
        MetricasTreino.Estagnado([S(1, (50, 10)), S(3, (50, 10)), S(5, (50, 10))]).Should().BeFalse();

    [Fact]
    public void Dupla_progressao_sugere_incremento_do_aparelho_quando_bate_o_topo() =>
        MetricasTreino.SugestaoCarga([new(50, 12), new(50, 12), new(50, 12)], 12, 5).Should().Be(55);

    [Fact]
    public void Dupla_progressao_sem_incremento_sobe_entre_2_5_e_5_porcento()
    {
        var s = MetricasTreino.SugestaoCarga([new(60, 12), new(60, 12)], 12, null)!.Value;
        s.Should().BeInRange(60 * 1.025m, 60 * 1.05m);
    }

    [Fact]
    public void Dupla_progressao_nao_sobe_se_alguma_serie_ficou_abaixo_do_topo() =>
        MetricasTreino.SugestaoCarga([new(50, 12), new(50, 10)], 12, 5).Should().BeNull();

    [Fact]
    public void Leitura_forca_subindo_e_cintura_estavel_indica_estrategia_funcionando() =>
        LeituraTreino.Interpretar(3, 0).Should().Contain("funcionando");

    [Fact]
    public void Leitura_forca_parada_e_cintura_subindo_indica_superavit_alto() =>
        LeituraTreino.Interpretar(0, 1.5m).Should().Contain("superávit");

    [Fact]
    public void Leitura_sem_dados_e_nula() => LeituraTreino.Interpretar(null, 1).Should().BeNull();
}

public class GorduraCorporalTests
{
    [Fact]
    public void Marinha_homem_valor_plausivel()
    {
        var bf = GorduraCorporal.MarinhaAmericana(Sexo.Masculino, 170, 82, 37);
        bf.Should().NotBeNull();
        bf!.Value.Should().BeInRange(14, 18);
    }

    [Fact]
    public void Marinha_mulher_exige_quadril() =>
        GorduraCorporal.MarinhaAmericana(Sexo.Feminino, 165, 70, 32).Should().BeNull();

    [Fact]
    public void Marinha_mulher_com_quadril() =>
        GorduraCorporal.MarinhaAmericana(Sexo.Feminino, 165, 70, 32, 98).Should().BeInRange(20, 35);

    [Fact]
    public void Medidas_impossiveis_retornam_nulo() =>
        GorduraCorporal.MarinhaAmericana(Sexo.Masculino, 170, 35, 40).Should().BeNull();
}

public class TextoBuscaTests
{
    [Theory]
    [InlineData("Feijão, carioca, cozido", "feijao carioca cozido")]
    [InlineData("  ÓLEO de  Soja ", "oleo de soja")]
    [InlineData("Maçã-Fuji", "maca fuji")]
    [InlineData(null, "")]
    public void Normaliza_minusculas_sem_acento(string? entrada, string esperado) =>
        TextoBusca.Normalizar(entrada).Should().Be(esperado);

    [Fact]
    public void Similaridade_identica_e_1() => TextoBusca.Similaridade("arroz", "Arroz").Should().Be(1);

    [Fact]
    public void Similaridade_ordena_candidatos_coerentes()
    {
        var arroz = TextoBusca.Similaridade("arroz branco cozido", "Arroz, tipo 1, cozido");
        var feijao = TextoBusca.Similaridade("arroz branco cozido", "Feijão, carioca, cozido");
        arroz.Should().BeGreaterThan(feijao);
    }

    [Fact]
    public void Similaridade_de_consulta_mede_cobertura_da_consulta() =>
        TextoBusca.SimilaridadeConsulta("frango", "Frango, peito, sem pele, grelhado").Should().Be(1);
}
