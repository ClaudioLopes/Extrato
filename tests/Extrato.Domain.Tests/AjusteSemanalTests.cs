using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;

namespace Extrato.Domain.Tests;

public class AjusteSemanalTests
{
    private static readonly DateOnly Ref = new(2026, 11, 16);

    /// <summary>Peso diário variando linearmente por semana, com 21 dias de histórico.</summary>
    private static List<(DateOnly, decimal)> Pesos(decimal inicial, decimal deltaSemana, int dias = 21) =>
        Enumerable.Range(0, dias)
            .Select(i => (Ref.AddDays(-(dias - 1) + i), inicial + deltaSemana * i / 7m))
            .ToList();

    private static Dictionary<DateOnly, decimal> Ingestao(int dias, decimal kcal) =>
        Enumerable.Range(0, dias).ToDictionary(i => Ref.AddDays(-i), _ => kcal);

    private static EntradaAjusteSemanal Entrada(List<(DateOnly, decimal)> pesos, Dictionary<DateOnly, decimal> ingestao,
        Objetivo obj = Objetivo.GanhoMassa, DateOnly? ultimo = null, int meta = 2830) =>
        new(Ref, obj, meta, 1632.5m, pesos, ingestao, ultimo, new Travas());

    [Fact]
    public void Get_adaptativo_segue_a_formula()
    {
        // comeu 2800/dia e ganhou 0,35 kg na semana → GET ≈ 2800 − 0,35×7700/7 = 2415
        AjusteSemanal.GetAdaptativo(2800, 0.35m).Should().Be(2415);
    }

    [Fact]
    public void Media_movel_de_7_dias_ignora_dias_sem_registro()
    {
        var pesos = new List<(DateOnly, decimal)> { (Ref.AddDays(-8), 80), (Ref.AddDays(-3), 72), (Ref, 74) };
        MediaMovel.Em(pesos, Ref).Should().Be(73);
        MediaMovel.Em(pesos, Ref.AddDays(-20)).Should().BeNull();
    }

    [Fact]
    public void Menos_de_5_dias_registrados_nao_ajusta()
    {
        var r = AjusteSemanal.Avaliar(Entrada(Pesos(72, 0), Ingestao(4, 2800)));
        r.Ajustar.Should().BeFalse();
        r.Motivo.Should().Contain("5");
    }

    [Fact]
    public void Sem_pesos_nao_ajusta()
    {
        var r = AjusteSemanal.Avaliar(Entrada([], Ingestao(7, 2800)));
        r.Ajustar.Should().BeFalse();
    }

    [Fact]
    public void Ritmo_dentro_da_faixa_nao_ajusta_mas_estima_o_get()
    {
        // ganho de massa: +0,25 kg/semana em 72 kg ≈ +0,35%
        var r = AjusteSemanal.Avaliar(Entrada(Pesos(72, 0.25m), Ingestao(7, 2830)));
        r.Ajustar.Should().BeFalse();
        r.GetEstimado.Should().NotBeNull();
        r.RitmoPercentSemana.Should().BeInRange(0.25m, 0.5m);
    }

    [Fact]
    public void Abaixo_da_faixa_por_duas_semanas_sobe_a_meta()
    {
        // ganho de massa com peso parado → precisa subir calorias
        var r = AjusteSemanal.Avaliar(Entrada(Pesos(72, 0), Ingestao(7, 2830)));
        r.Ajustar.Should().BeTrue();
        r.KcalNova.Should().Be(2830 + AjusteSemanal.PassoKcal);
        r.Motivo.Should().Contain("abaixo");
    }

    [Fact]
    public void Acima_da_faixa_por_duas_semanas_desce_a_meta()
    {
        // ganho de massa rápido demais (+0,7 kg/semana ≈ +1%)
        var r = AjusteSemanal.Avaliar(Entrada(Pesos(72, 0.7m), Ingestao(7, 3200), meta: 3200));
        r.Ajustar.Should().BeTrue();
        r.KcalNova.Should().BeLessThan(3200);
        (3200 - r.KcalNova).Should().BeLessThanOrEqualTo(150);
    }

    [Fact]
    public void Fora_da_faixa_so_nesta_semana_aguarda_confirmacao()
    {
        // semana anterior ganhando bem, esta semana parado
        var pesos = Enumerable.Range(0, 21).Select(i =>
        {
            var d = Ref.AddDays(-20 + i);
            var kg = i < 14 ? 72 + 0.3m * i / 7m : 72 + 0.6m;
            return (d, kg);
        }).ToList();
        var r = AjusteSemanal.Avaliar(Entrada(pesos, Ingestao(7, 2830)));
        r.Ajustar.Should().BeFalse();
        r.Motivo.Should().Contain("segunda semana");
    }

    [Fact]
    public void No_maximo_um_ajuste_por_semana()
    {
        var r = AjusteSemanal.Avaliar(Entrada(Pesos(72, 0), Ingestao(7, 2830), ultimo: Ref.AddDays(-3)));
        r.Ajustar.Should().BeFalse();
        r.Motivo.Should().Contain("1 ajuste por semana");
    }

    [Fact]
    public void Definicao_nunca_desce_abaixo_da_TMB()
    {
        var r = AjusteSemanal.Avaliar(Entrada(Pesos(72, 0), Ingestao(7, 1650), Objetivo.Definicao, meta: 1650));
        r.KcalNova.Should().BeGreaterThanOrEqualTo(1633);
    }

    [Fact]
    public void Leitura_de_treino_entra_no_motivo()
    {
        var e = Entrada(Pesos(72, 0), Ingestao(7, 2830)) with { LeituraTreino = "Força subindo." };
        AjusteSemanal.Avaliar(e).Motivo.Should().Contain("Força subindo.");
    }

    [Theory]
    [InlineData(Objetivo.GanhoMassa, 0.25, 0.5)]
    [InlineData(Objetivo.Recomposicao, -0.25, 0.25)]
    [InlineData(Objetivo.Definicao, -1.0, -0.5)]
    public void Faixas_de_ritmo_por_objetivo(Objetivo obj, double min, double max) =>
        AjusteSemanal.FaixaRitmo(obj).Should().Be(((decimal)min, (decimal)max));
}
