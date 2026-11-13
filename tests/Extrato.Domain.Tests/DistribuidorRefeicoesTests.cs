using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;

namespace Extrato.Domain.Tests;

public class DistribuidorRefeicoesTests
{
    private static readonly Macros Meta = new(2800, 140, 400, 70);
    private static readonly PesosRefeicao Pesos = PesosRefeicao.Padrao;

    [Fact]
    public void Sem_consumo_cada_refeicao_recebe_seu_peso()
    {
        var alvos = DistribuidorRefeicoes.Distribuir(Meta, Pesos, new Dictionary<TipoRefeicao, Macros>());
        alvos.Select(a => a.Alvo.Kcal).Should().Equal(700, 980, 420, 700);
        alvos.Sum(a => a.Alvo.Kcal).Should().Be(Meta.Kcal);
    }

    [Fact]
    public void Restante_e_redistribuido_proporcional_aos_pesos_das_refeicoes_que_faltam()
    {
        // café comeu menos: sobra mais para o resto
        var consumido = new Dictionary<TipoRefeicao, Macros> { [TipoRefeicao.CafeDaManha] = new(400, 20, 50, 10) };
        var alvos = DistribuidorRefeicoes.Distribuir(Meta, Pesos, consumido);

        var restante = 2800 - 400;
        alvos.Single(a => a.Tipo == TipoRefeicao.Almoco).Alvo.Kcal.Should().Be(restante * 0.35m / 0.75m);
        alvos.Single(a => a.Tipo == TipoRefeicao.Tarde).Alvo.Kcal.Should().Be(restante * 0.15m / 0.75m);
        alvos.Where(a => !a.Registrada).Sum(a => a.Alvo.Kcal).Should().BeApproximately(restante, 0.5m);
    }

    [Fact]
    public void Refeicao_registrada_mostra_consumido_e_mantem_alvo_original()
    {
        var consumido = new Dictionary<TipoRefeicao, Macros> { [TipoRefeicao.Almoco] = new(1200, 60, 140, 30) };
        var almoco = DistribuidorRefeicoes.Distribuir(Meta, Pesos, consumido).Single(a => a.Tipo == TipoRefeicao.Almoco);
        almoco.Registrada.Should().BeTrue();
        almoco.Consumido.Kcal.Should().Be(1200);
        almoco.AlvoOriginal.Kcal.Should().Be(980);
    }

    [Fact]
    public void Dia_estourado_cai_ate_o_piso_e_nunca_sugere_pular_refeicao()
    {
        var consumido = new Dictionary<TipoRefeicao, Macros>
        {
            [TipoRefeicao.CafeDaManha] = new(1500, 50, 200, 50),
            [TipoRefeicao.Almoco] = new(1500, 60, 200, 50),
        };
        var alvos = DistribuidorRefeicoes.Distribuir(Meta, Pesos, consumido, piso: 0.5m);
        var tarde = alvos.Single(a => a.Tipo == TipoRefeicao.Tarde);
        var noite = alvos.Single(a => a.Tipo == TipoRefeicao.Noite);

        tarde.Alvo.Kcal.Should().Be(210);   // 50% de 420
        noite.Alvo.Kcal.Should().Be(350);   // 50% de 700
        tarde.NoPiso.Should().BeTrue();
        alvos.Where(a => !a.Registrada).Should().OnlyContain(a => a.Alvo.Kcal > 0);
    }

    [Fact]
    public void Meta_do_dia_nao_muda_por_causa_de_uma_refeicao()
    {
        var consumido = new Dictionary<TipoRefeicao, Macros> { [TipoRefeicao.CafeDaManha] = new(2000, 50, 300, 50) };
        var alvos = DistribuidorRefeicoes.Distribuir(Meta, Pesos, consumido);
        alvos.Sum(a => a.AlvoOriginal.Kcal).Should().Be(Meta.Kcal);
    }

    [Fact]
    public void Pesos_personalizados_sao_normalizados()
    {
        var pesos = new PesosRefeicao(1, 1, 1, 1);
        var alvos = DistribuidorRefeicoes.Distribuir(Meta, pesos, new Dictionary<TipoRefeicao, Macros>());
        alvos.Should().OnlyContain(a => a.Alvo.Kcal == 700);
    }

    [Fact]
    public void Pesos_zerados_sao_invalidos()
    {
        var act = () => DistribuidorRefeicoes.Distribuir(Meta, new PesosRefeicao(0, 0, 0, 0), new Dictionary<TipoRefeicao, Macros>());
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(17, 60, false)] // antes do horário
    [InlineData(18, 60, true)]  // < 50% no fim da tarde
    [InlineData(18, 75, false)] // já passou de 50%
    [InlineData(21, 0, true)]
    public void Alerta_de_proteina_atrasada(int hora, decimal consumida, bool esperado) =>
        DistribuidorRefeicoes.ProteinaAtrasada(144, consumida, new TimeOnly(hora, 0)).Should().Be(esperado);

    [Fact]
    public void Macros_por_porcao_usam_gramas_sobre_100()
    {
        var m = Macros.DePorcao(150, 128, 2.5m, 28.1m, 0.2m);
        m.Should().Be(new Macros(192, 3.8m, 42.2m, 0.3m));
    }
}
