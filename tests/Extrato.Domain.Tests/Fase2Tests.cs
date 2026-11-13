using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;

namespace Extrato.Domain.Tests;

public class CicloTreinoTests
{
    private static readonly Macros Base = new(2800, 144, 400, 65);

    private static ConfiguracaoRegras Regras(bool ativo = true, int extra = 200, params DayOfWeek[] dias) => new()
    {
        CicloTreinoAtivo = ativo,
        KcalExtraDiaTreino = extra,
        DiasTreino = dias.Length > 0 ? [.. dias] : [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
    };

    [Fact]
    public void Desligado_nao_muda_a_meta()
    {
        var r = CicloTreino.Aplicar(Base, Regras(ativo: false), diaDeTreino: true, tmb: 1600);
        r.Macros.Should().Be(Base);
        r.DeltaKcal.Should().Be(0);
    }

    [Fact]
    public void Dia_de_treino_ganha_carboidrato()
    {
        var r = CicloTreino.Aplicar(Base, Regras(), diaDeTreino: true, tmb: 1600);
        r.Macros.Kcal.Should().Be(3000);
        r.Macros.CarboG.Should().Be(450);
        r.Macros.ProteinaG.Should().Be(Base.ProteinaG);
        r.Macros.GorduraG.Should().Be(Base.GorduraG);
    }

    [Fact]
    public void Descanso_compensa_e_a_media_da_semana_nao_muda()
    {
        var regras = Regras();
        var treino = CicloTreino.Aplicar(Base, regras, true, 1600).Macros.Kcal;
        var descanso = CicloTreino.Aplicar(Base, regras, false, 1600).Macros.Kcal;
        descanso.Should().Be(2300); // 200 × 5 / 2 = 500
        (treino * 5 + descanso * 2).Should().Be(Base.Kcal * 7);
    }

    [Theory]
    [InlineData(200, 5, 500)]
    [InlineData(150, 3, 113)]
    [InlineData(200, 0, 0)]
    [InlineData(200, 7, 0)]
    public void Reducao_no_descanso(int extra, int dias, int esperado) =>
        CicloTreino.ReducaoDescanso(extra, dias).Should().Be(esperado);

    [Fact]
    public void Descanso_nunca_fica_abaixo_da_TMB()
    {
        var baixa = new Macros(1800, 140, 200, 55);
        var r = CicloTreino.Aplicar(baixa, Regras(extra: 400), false, tmb: 1700);
        r.Macros.Kcal.Should().BeGreaterThanOrEqualTo(1700);
    }

    [Fact]
    public void Sessao_registrada_conta_como_dia_de_treino()
    {
        var sabado = new DateOnly(2026, 11, 14);
        CicloTreino.EhDiaDeTreino(sabado, [DayOfWeek.Monday], houveSessao: false).Should().BeFalse();
        CicloTreino.EhDiaDeTreino(sabado, [DayOfWeek.Monday], houveSessao: true).Should().BeTrue();
        CicloTreino.EhDiaDeTreino(sabado, [DayOfWeek.Saturday], houveSessao: false).Should().BeTrue();
    }
}

public class MontadorCardapioTests
{
    private static CandidatoCardapio C(int id, string nome, decimal kcal, decimal p, decimal c, decimal g, decimal porcao = 100, decimal pref = 0.2m)
    {
        var m = new Macros(kcal, p, c, g);
        var papel = MontadorCardapio.Classificar(m);
        var (min, max) = papel == PapelAlimento.Complemento ? (porcao, porcao) : (30m, 300m);
        return new CandidatoCardapio(id, nome, m, papel, min, max, porcao, pref);
    }

    private static readonly CandidatoCardapio Frango = C(1, "Frango", 159, 32, 0, 2.5m);
    private static readonly CandidatoCardapio Patinho = C(2, "Patinho", 219, 35.9m, 0, 7.3m);
    private static readonly CandidatoCardapio Arroz = C(3, "Arroz", 128, 2.5m, 28.1m, 0.2m);
    private static readonly CandidatoCardapio Batata = C(4, "Batata", 52, 1.2m, 11.9m, 0);
    private static readonly CandidatoCardapio Feijao = C(5, "Feijão", 76, 4.8m, 13.6m, 0.5m);
    private static readonly CandidatoCardapio Tomate = C(6, "Tomate", 15, 1.1m, 3.1m, 0.2m, porcao: 50);

    [Theory]
    [InlineData(159, 32, 0, 2.5, PapelAlimento.Proteina)]
    [InlineData(128, 2.5, 28.1, 0.2, PapelAlimento.Carboidrato)]
    [InlineData(884, 0, 0, 100, PapelAlimento.Complemento)]
    public void Classifica_pelo_macro_dominante(decimal kcal, decimal p, decimal c, decimal g, PapelAlimento esperado) =>
        MontadorCardapio.Classificar(new Macros(kcal, p, c, g)).Should().Be(esperado);

    [Fact]
    public void Monta_almoco_proximo_do_alvo()
    {
        var alvo = new Macros(990, 50, 146, 23);
        var opcoes = MontadorCardapio.Montar(alvo, [Frango, Patinho, Arroz, Batata, Feijao, Tomate]);

        opcoes.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(3);
        var melhor = opcoes[0];
        melhor.Total.ProteinaG.Should().BeApproximately(50, 8);
        melhor.Total.Kcal.Should().BeLessThan(alvo.Kcal * 1.15m);
        melhor.Itens.Should().OnlyContain(i => i.Gramas % 5 == 0 && i.Gramas > 0);
    }

    [Fact]
    public void Opcoes_variam_a_fonte_de_proteina()
    {
        var opcoes = MontadorCardapio.Montar(new Macros(900, 50, 120, 20), [Frango, Patinho, Arroz, Feijao]);
        opcoes.Select(o => o.Itens[0].AlimentoId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Preferencia_da_rotina_desempata()
    {
        // dois alimentos de encaixe idêntico: vence o que a pessoa costuma comer
        var frangoHabito = Frango with { Preferencia = 1m };
        var outroFrango = Frango with { AlimentoId = 99, Nome = "Frango (outro corte)", Preferencia = 0.2m };
        var opcoes = MontadorCardapio.Montar(new Macros(700, 45, 90, 10), [outroFrango, frangoHabito, Arroz]);
        opcoes[0].Itens[0].Nome.Should().Be("Frango");
    }

    [Fact]
    public void Prato_de_costume_vem_primeiro_mesmo_encaixando_pior()
    {
        var frangoHabito = Frango with { Preferencia = 1m };
        var arrozHabito = Arroz with { Preferencia = 1m };
        var opcoes = MontadorCardapio.Montar(new Macros(990, 50, 146, 23), [frangoHabito, Patinho, arrozHabito, Batata, Tomate]);
        opcoes[0].Itens.Select(i => i.Nome).Should().Contain(["Frango", "Arroz"]);
        opcoes.Skip(1).Should().NotBeEmpty("as alternativas continuam aparecendo");
    }

    [Fact]
    public void Macros_dos_itens_vem_da_tabela()
    {
        var o = MontadorCardapio.Montar(new Macros(700, 45, 90, 10), [Frango, Arroz])[0];
        foreach (var i in o.Itens)
        {
            var origem = i.AlimentoId == Frango.AlimentoId ? Frango : Arroz;
            i.Macros.Should().Be(origem.Por100g.Vezes(i.Gramas / 100m));
        }
    }

    [Fact]
    public void Sem_proteina_ou_carbo_ou_alvo_pequeno_nao_sugere()
    {
        MontadorCardapio.Montar(new Macros(700, 45, 90, 10), [Arroz, Feijao]).Should().BeEmpty();
        MontadorCardapio.Montar(new Macros(50, 3, 5, 1), [Frango, Arroz]).Should().BeEmpty();
    }

    [Fact]
    public void Erro_pune_mais_estourar_kcal_do_que_faltar()
    {
        var alvo = new Macros(600, 40, 60, 15);
        MontadorCardapio.Erro(alvo, alvo with { Kcal = 700 }).Should().BeGreaterThan(MontadorCardapio.Erro(alvo, alvo with { Kcal = 500 }));
        MontadorCardapio.Erro(alvo, alvo).Should().Be(0);
    }
}
