using Extrato.Domain.Regras;

namespace Extrato.Domain.Tests;

/// <summary>Ajuste semanal com a máquina desligada fora de uso: roda na primeira subida a partir de segunda, uma vez por semana.</summary>
public class AgendaAjusteSemanalTests
{
    private static readonly DateTime Segunda = new(2026, 11, 16, 9, 0, 0);

    [Fact]
    public void Inicio_da_semana_e_a_ultima_segunda_ate_hoje()
    {
        AgendaAjusteSemanal.InicioDaSemana(new DateOnly(2026, 11, 16), DayOfWeek.Monday).Should().Be(new DateOnly(2026, 11, 16));
        AgendaAjusteSemanal.InicioDaSemana(new DateOnly(2026, 11, 19), DayOfWeek.Monday).Should().Be(new DateOnly(2026, 11, 16));
        AgendaAjusteSemanal.InicioDaSemana(new DateOnly(2026, 11, 22), DayOfWeek.Monday).Should().Be(new DateOnly(2026, 11, 16), "domingo ainda é a semana da segunda anterior");
    }

    [Fact]
    public void Na_segunda_depois_das_6h_roda_se_nao_rodou_nesta_semana() =>
        AgendaAjusteSemanal.DeveRodar(Segunda, DayOfWeek.Monday, new DateOnly(2026, 11, 9)).Should().BeTrue();

    [Fact]
    public void Na_segunda_antes_das_6h_espera() =>
        AgendaAjusteSemanal.DeveRodar(Segunda.Date.AddHours(5), DayOfWeek.Monday, new DateOnly(2026, 11, 9)).Should().BeFalse();

    [Fact]
    public void Maquina_desligada_na_segunda_roda_na_quinta_a_qualquer_hora() =>
        AgendaAjusteSemanal.DeveRodar(new DateTime(2026, 11, 19, 3, 0, 0), DayOfWeek.Monday, new DateOnly(2026, 11, 9)).Should().BeTrue();

    [Fact]
    public void Ja_rodou_nesta_semana_nao_roda_de_novo_a_cada_religamento()
    {
        var ultima = new DateOnly(2026, 11, 17); // rodou na terça
        AgendaAjusteSemanal.DeveRodar(new DateTime(2026, 11, 18, 20, 0, 0), DayOfWeek.Monday, ultima).Should().BeFalse();
        AgendaAjusteSemanal.DeveRodar(new DateTime(2026, 11, 22, 23, 0, 0), DayOfWeek.Monday, ultima).Should().BeFalse("domingo, mesma semana");
        AgendaAjusteSemanal.DeveRodar(new DateTime(2026, 11, 23, 7, 0, 0), DayOfWeek.Monday, ultima).Should().BeTrue("segunda seguinte");
    }

    [Fact]
    public void Nunca_rodou_roda_na_primeira_subida() =>
        AgendaAjusteSemanal.DeveRodar(new DateTime(2026, 11, 14, 12, 0, 0), DayOfWeek.Monday, null).Should().BeTrue();

    [Fact]
    public void Respeita_o_dia_configurado() =>
        AgendaAjusteSemanal.DeveRodar(new DateTime(2026, 11, 20, 10, 0, 0), DayOfWeek.Friday, new DateOnly(2026, 11, 16)).Should().BeTrue();
}
