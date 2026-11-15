namespace Extrato.Infrastructure.Dados;

/// <summary>Última execução de uma tarefa automática (ex.: ajuste semanal): sobrevive aos religamentos da máquina.</summary>
public class ExecucaoJob
{
    public string Nome { get; set; } = "";
    public DateOnly UltimaExecucao { get; set; }
}
