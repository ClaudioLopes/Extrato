namespace Extrato.Domain.Entidades;

/// <summary>
/// Atividade fora da musculação (natação, vôlei…), lida do print do resumo da pulseira ou digitada.
/// Só os números são guardados; o print nunca é salvo.
/// </summary>
public class Atividade
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public DateTime DataHora { get; set; }
    public TipoAtividade Tipo { get; set; }
    /// <summary>Nome livre quando o tipo é Outra (ou detalhe, ex.: "vôlei de praia").</summary>
    public string? Descricao { get; set; }
    public int DuracaoMin { get; set; }
    /// <summary>Calorias ativas (acima do repouso): é o que entra na meta do dia.</summary>
    public int KcalAtivas { get; set; }
    /// <summary>Calorias totais da pulseira (incluem o metabolismo de repouso do período). Só informativo.</summary>
    public int? KcalTotais { get; set; }
    public int? DistanciaM { get; set; }
    public int? FcMedia { get; set; }
    public OrigemAtividade Origem { get; set; }
}
