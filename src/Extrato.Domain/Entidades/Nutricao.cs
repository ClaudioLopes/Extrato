namespace Extrato.Domain.Entidades;

// Nenhuma entidade tem coluna de imagem: fotos são processadas em memória e descartadas.

public class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public Sexo Sexo { get; set; }
    public DateOnly DataNascimento { get; set; }
    public decimal AlturaCm { get; set; }
    public NivelAtividade NivelAtividade { get; set; }
    public Objetivo Objetivo { get; set; }

    public ConfiguracaoRegras? Regras { get; set; }
    public List<AlimentoEvitado> AlimentosEvitados { get; set; } = [];

    public int IdadeEm(DateOnly data)
    {
        var idade = data.Year - DataNascimento.Year;
        if (DataNascimento > data.AddYears(-idade)) idade--;
        return idade;
    }
}

public class ConfiguracaoRegras
{
    public int UsuarioId { get; set; }
    public decimal ProteinaGPorKg { get; set; } = 2.0m;
    public decimal GorduraGPorKg { get; set; } = 0.9m;
    /// <summary>Ajuste sobre o GET. Nulo = padrão do objetivo.</summary>
    public int? AjusteKcal { get; set; }
    /// <summary>Fator de atividade manual. Nulo = tabela por nível.</summary>
    public decimal? FatorAtividade { get; set; }
    public PesosRefeicao PesosRefeicao { get; set; } = PesosRefeicao.Padrao;
    public Travas Travas { get; set; } = new();
    /// <summary>Hora a partir da qual a proteína abaixo de 50% gera alerta.</summary>
    public int HoraAlertaProteina { get; set; } = 18;
    /// <summary>Piso das refeições restantes quando o dia estoura (fração do alvo original).</summary>
    public decimal PisoRefeicao { get; set; } = 0.5m;
    /// <summary>Fase 2: mais carboidrato em dia de treino, menos no descanso (média semanal igual).</summary>
    public bool CicloTreinoAtivo { get; set; }
    public int KcalExtraDiaTreino { get; set; } = 200;
    /// <summary>Dias planejados de treino (dia com sessão registrada também conta).</summary>
    public List<DayOfWeek> DiasTreino { get; set; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
    /// <summary>Atividades registradas (natação, vôlei…) somam calorias à meta do dia em que aconteceram.</summary>
    public bool AtividadesNaMeta { get; set; } = true;
    /// <summary>Parte das calorias ativas da pulseira que entra na meta (pulseiras costumam superestimar).</summary>
    public int PercentualKcalAtividade { get; set; } = 75;

    public static ConfiguracaoRegras PadraoPara(int usuarioId) => new() { UsuarioId = usuarioId };
}

public record PesosRefeicao(decimal Cafe, decimal Almoco, decimal Tarde, decimal Noite)
{
    public static PesosRefeicao Padrao => new(0.25m, 0.35m, 0.15m, 0.25m);

    public decimal Para(TipoRefeicao tipo) => tipo switch
    {
        TipoRefeicao.CafeDaManha => Cafe,
        TipoRefeicao.Almoco => Almoco,
        TipoRefeicao.Tarde => Tarde,
        TipoRefeicao.Noite => Noite,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo))
    };

    public decimal Soma => Cafe + Almoco + Tarde + Noite;
}

public record Travas
{
    public decimal ProteinaMaxGPorKg { get; init; } = 2.5m;
    public int VariacaoMaxKcalSemana { get; init; } = 150;
    /// <summary>Superávit máximo sobre o GET.</summary>
    public int SuperavitMaxKcal { get; init; } = 500;
    /// <summary>Déficit máximo sobre o GET.</summary>
    public int DeficitMaxKcal { get; init; } = 500;
}

public class MetaDiaria
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public DateOnly VigenteDesde { get; set; }
    public int Kcal { get; set; }
    public decimal ProteinaG { get; set; }
    public decimal CarboG { get; set; }
    public decimal GorduraG { get; set; }
    public OrigemMeta Origem { get; set; }
    public DateTime CriadaEm { get; set; } = DateTime.UtcNow;

    public Macros ComoMacros() => new(Kcal, ProteinaG, CarboG, GorduraG);
}

public class AjusteMeta
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public DateOnly Data { get; set; }
    public int KcalAnterior { get; set; }
    public int KcalNova { get; set; }
    public int? GetEstimado { get; set; }
    public string Motivo { get; set; } = "";
}

public class Alimento
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string NomeNormalizado { get; set; } = "";
    public FonteAlimento Fonte { get; set; }
    public decimal Kcal100g { get; set; }
    public decimal Proteina100g { get; set; }
    public decimal Carbo100g { get; set; }
    public decimal Gordura100g { get; set; }
    public decimal? Fibra100g { get; set; }
    public List<AlimentoSinonimo> Sinonimos { get; set; } = [];

    public Macros MacrosPara(decimal gramas) => Macros.DePorcao(gramas, Kcal100g, Proteina100g, Carbo100g, Gordura100g);
}

public class AlimentoSinonimo
{
    public int AlimentoId { get; set; }
    public string Termo { get; set; } = "";
}

public class AlimentoEvitado
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public int? AlimentoId { get; set; }
    public string? Termo { get; set; }
}

public class PorcaoFavorita
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public string Nome { get; set; } = "";
    public TipoRefeicao? TipoSugerido { get; set; }
    public List<PorcaoFavoritaItem> Itens { get; set; } = [];
}

public class PorcaoFavoritaItem
{
    public int Id { get; set; }
    public int PorcaoFavoritaId { get; set; }
    public int AlimentoId { get; set; }
    public Alimento? Alimento { get; set; }
    public decimal Gramas { get; set; }
}

public class Refeicao
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public DateTime DataHora { get; set; }
    public TipoRefeicao Tipo { get; set; }
    public OrigemRefeicao Origem { get; set; }
    public string? DescricaoUsuario { get; set; }
    /// <summary>Id gerado no aparelho para lançamentos offline: reenvio não duplica.</summary>
    public Guid? IdCliente { get; set; }
    public List<ItemRefeicao> Itens { get; set; } = [];

    public Macros Total() => Macros.Somar(Itens.Select(i => i.Macros()));
}

public class ItemRefeicao
{
    public int Id { get; set; }
    public int RefeicaoId { get; set; }
    public int AlimentoId { get; set; }
    public Alimento? Alimento { get; set; }
    public decimal Gramas { get; set; }
    public decimal Kcal { get; set; }
    public decimal ProteinaG { get; set; }
    public decimal CarboG { get; set; }
    public decimal GorduraG { get; set; }
    public decimal? GramasSugeridasIA { get; set; }
    public decimal? ConfiancaIA { get; set; }
    public bool EditadoPeloUsuario { get; set; }

    public Macros Macros() => new(Kcal, ProteinaG, CarboG, GorduraG);

    /// <summary>Macros sempre calculados no backend a partir da tabela (nunca da IA).</summary>
    public void Calcular(Alimento alimento)
    {
        var m = alimento.MacrosPara(Gramas);
        Kcal = m.Kcal; ProteinaG = m.ProteinaG; CarboG = m.CarboG; GorduraG = m.GorduraG;
        EditadoPeloUsuario = GramasSugeridasIA.HasValue && GramasSugeridasIA.Value != Gramas;
    }
}

public class RegistroPeso
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public DateOnly Data { get; set; }
    public decimal PesoKg { get; set; }
}

public class Medidas
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public DateOnly Data { get; set; }
    public decimal CinturaCm { get; set; }
    public decimal PescocoCm { get; set; }
    public decimal? QuadrilCm { get; set; }
    public string? ObservacaoCorporal { get; set; }
    /// <summary>Fase 2: faixa de % de gordura estimada por foto (a foto em si nunca é guardada).</summary>
    public decimal? GorduraFotoMinPercent { get; set; }
    public decimal? GorduraFotoMaxPercent { get; set; }
}

/// <summary>Acesso do único usuário. Só o hash da senha (PBKDF2) é guardado.</summary>
public class Credencial
{
    public int UsuarioId { get; set; }
    public string Login { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public DateTime CriadaEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadaEm { get; set; } = DateTime.UtcNow;
}
