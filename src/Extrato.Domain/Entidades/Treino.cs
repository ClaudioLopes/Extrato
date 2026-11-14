namespace Extrato.Domain.Entidades;

public class Academia
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
}

/// <summary>Carga de máquina não é comparável entre academias: a evolução é sempre por aparelho.</summary>
public class Aparelho
{
    public int Id { get; set; }
    public int AcademiaId { get; set; }
    public Academia? Academia { get; set; }
    public string Nome { get; set; } = "";
    public string Exercicio { get; set; } = "";
    public string GrupoMuscular { get; set; } = "";
    public TipoCarga TipoCarga { get; set; }
    public decimal? IncrementoKg { get; set; }
}

public class Rotina
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public List<RotinaItem> Itens { get; set; } = [];
}

public class RotinaItem
{
    public int Id { get; set; }
    public int RotinaId { get; set; }
    public int AparelhoId { get; set; }
    public Aparelho? Aparelho { get; set; }
    public int Ordem { get; set; }
    public int SeriesAlvo { get; set; }
    public int RepsMin { get; set; }
    public int RepsMax { get; set; }
}

public class SessaoTreino
{
    public int Id { get; set; }
    /// <summary>Gerado no aparelho: reenvios da fila offline não duplicam a sessão.</summary>
    public Guid? IdCliente { get; set; }
    public DateTime DataHora { get; set; }
    public int? RotinaId { get; set; }
    public Rotina? Rotina { get; set; }
    public string? Observacao { get; set; }
    public List<Serie> Series { get; set; } = [];
}

public class Serie
{
    public int Id { get; set; }
    /// <summary>Gerado no aparelho: reenvios da fila offline não duplicam a série.</summary>
    public Guid? IdCliente { get; set; }
    public int SessaoId { get; set; }
    public SessaoTreino? Sessao { get; set; }
    public int AparelhoId { get; set; }
    public Aparelho? Aparelho { get; set; }
    public int Ordem { get; set; }
    public decimal CargaKg { get; set; }
    public int Reps { get; set; }
    public int? RepsNaReserva { get; set; }
}
