using System.ComponentModel.DataAnnotations;
using Extrato.Domain;

namespace Extrato.Contracts;

// ---------- Comuns ----------

public record MacrosDto(decimal Kcal, decimal ProteinaG, decimal CarboG, decimal GorduraG)
{
    public static MacrosDto De(Macros m) => new(m.Kcal, m.ProteinaG, m.CarboG, m.GorduraG);
    public static MacrosDto Zero => new(0, 0, 0, 0);
}

// ---------- Perfil e regras ----------

public class PerfilDto
{
    [Required, StringLength(80)] public string Nome { get; set; } = "";
    public Sexo Sexo { get; set; } = Sexo.Masculino;
    public DateOnly DataNascimento { get; set; } = new(1995, 1, 1);
    [Range(100, 250)] public decimal AlturaCm { get; set; } = 170;
    public NivelAtividade NivelAtividade { get; set; } = NivelAtividade.Moderado;
    public Objetivo Objetivo { get; set; } = Objetivo.GanhoMassa;
    /// <summary>Último peso registrado (leitura) ou peso a registrar hoje (escrita).</summary>
    [Range(30, 300)] public decimal? PesoAtualKg { get; set; }
    public List<string> AlimentosEvitados { get; set; } = [];
}

public class PesosRefeicaoDto
{
    [Range(0, 1)] public decimal Cafe { get; set; } = 0.25m;
    [Range(0, 1)] public decimal Almoco { get; set; } = 0.35m;
    [Range(0, 1)] public decimal Tarde { get; set; } = 0.15m;
    [Range(0, 1)] public decimal Noite { get; set; } = 0.25m;
}

public class TravasDto
{
    [Range(1.0, 3.5)] public decimal ProteinaMaxGPorKg { get; set; } = 2.5m;
    [Range(50, 300)] public int VariacaoMaxKcalSemana { get; set; } = 150;
    [Range(0, 1000)] public int SuperavitMaxKcal { get; set; } = 500;
    [Range(0, 1000)] public int DeficitMaxKcal { get; set; } = 500;
}

public class RegrasDto
{
    [Range(1.0, 3.5)] public decimal ProteinaGPorKg { get; set; } = 2.0m;
    [Range(0.5, 1.5)] public decimal GorduraGPorKg { get; set; } = 0.9m;
    [Range(-1000, 1000)] public int? AjusteKcal { get; set; }
    [Range(1.0, 2.5)] public decimal? FatorAtividade { get; set; }
    public PesosRefeicaoDto PesosRefeicao { get; set; } = new();
    public TravasDto Travas { get; set; } = new();
    [Range(0, 23)] public int HoraAlertaProteina { get; set; } = 18;
    [Range(0, 1)] public decimal PisoRefeicao { get; set; } = 0.5m;
    public bool CicloTreinoAtivo { get; set; }
    [Range(0, 600)] public int KcalExtraDiaTreino { get; set; } = 200;
    public List<DayOfWeek> DiasTreino { get; set; } = [];
    /// <summary>Atividades registradas (natação, vôlei…) somam à meta do dia.</summary>
    public bool AtividadesNaMeta { get; set; } = true;
    [Range(0, 100)] public int PercentualKcalAtividade { get; set; } = 75;
}

// ---------- Atividades (pulseira) ----------

public class NovaAtividadeDto
{
    public DateTime? DataHora { get; set; }
    public TipoAtividade Tipo { get; set; }
    [StringLength(100)] public string? Descricao { get; set; }
    [Range(1, 600)] public int DuracaoMin { get; set; }
    /// <summary>Vazio: estimado pela tabela MET com o peso atual.</summary>
    [Range(0, 5000)] public int? KcalAtivas { get; set; }
    [Range(0, 6000)] public int? KcalTotais { get; set; }
    [Range(0, 200000)] public int? DistanciaM { get; set; }
    [Range(30, 230)] public int? FcMedia { get; set; }
    public OrigemAtividade Origem { get; set; } = OrigemAtividade.Manual;
}

public record AtividadeDto(int Id, DateTime DataHora, TipoAtividade Tipo, string? Descricao, int DuracaoMin, int KcalAtivas,
    int? KcalTotais, int? DistanciaM, int? FcMedia, OrigemAtividade Origem, int KcalNaMeta);

/// <summary>Leitura do print para o usuário conferir; nada é gravado até confirmar.</summary>
public record RascunhoAtividadeDto(
    NovaAtividadeDto Atividade,
    bool Reconhecido,
    IReadOnlyList<string> Alertas,
    bool FalhaIA,
    string? MensagemFalha);

// ---------- Metas ----------

public record MetaDto(int Id, DateOnly VigenteDesde, int Kcal, decimal ProteinaG, decimal CarboG, decimal GorduraG, OrigemMeta Origem);

public record CalculoMetaDto(decimal Tmb, decimal FatorAtividade, decimal Get, int AjusteKcal, MetaDto Meta, IReadOnlyList<string> Racional);

public class MetaManualDto
{
    [Range(800, 6000)] public int Kcal { get; set; }
    [Range(0, 500)] public decimal ProteinaG { get; set; }
    [Range(0, 1000)] public decimal CarboG { get; set; }
    [Range(0, 300)] public decimal GorduraG { get; set; }
}

public record AjusteSemanalDto(bool Ajustou, int KcalAnterior, int KcalNova, int? GetEstimado, decimal? RitmoPercentSemana, string Motivo);

public record AjusteMetaDto(int Id, DateOnly Data, int KcalAnterior, int KcalNova, int? GetEstimado, string Motivo);

// ---------- Alimentos ----------

public record AlimentoDto(int Id, string Nome, FonteAlimento Fonte, decimal Kcal100g, decimal Proteina100g, decimal Carbo100g, decimal Gordura100g, decimal? Fibra100g, double? Relevancia = null);

public class NovoAlimentoDto
{
    [Required, StringLength(150)] public string Nome { get; set; } = "";
    [Range(0, 900)] public decimal Kcal100g { get; set; }
    [Range(0, 100)] public decimal Proteina100g { get; set; }
    [Range(0, 100)] public decimal Carbo100g { get; set; }
    [Range(0, 100)] public decimal Gordura100g { get; set; }
    [Range(0, 100)] public decimal? Fibra100g { get; set; }
    public List<string> Sinonimos { get; set; } = [];
}

public record ImportacaoDto(int Inseridos, int Atualizados, IReadOnlyList<string> Erros);

// ---------- Refeições ----------

public record ItemRascunhoDto(
    string NomeIA,
    string? Preparo,
    decimal GramasSugeridas,
    decimal Confianca,
    string? Observacao,
    int? AlimentoId,
    string? AlimentoNome,
    MacrosDto? Macros,
    bool NaoIdentificado,
    bool BaixaConfianca,
    bool Evitado);

public record RascunhoRefeicaoDto(
    TipoRefeicao Tipo,
    string? Descricao,
    IReadOnlyList<ItemRascunhoDto> Itens,
    IReadOnlyList<string> Alertas,
    bool FalhaIA,
    string? MensagemFalha,
    string Provedor);

public class NovoItemRefeicaoDto
{
    public int AlimentoId { get; set; }
    [Range(1, 1500)] public decimal Gramas { get; set; }
    public decimal? GramasSugeridasIA { get; set; }
    [Range(0, 1)] public decimal? ConfiancaIA { get; set; }
}

public class NovaRefeicaoDto
{
    /// <summary>Gerado no aparelho; reenvios com o mesmo id devolvem a refeição já gravada.</summary>
    public Guid? IdCliente { get; set; }
    public DateTime? DataHora { get; set; }
    public TipoRefeicao Tipo { get; set; }
    public OrigemRefeicao Origem { get; set; } = OrigemRefeicao.Manual;
    [StringLength(500)] public string? Descricao { get; set; }
    [MinLength(1)] public List<NovoItemRefeicaoDto> Itens { get; set; } = [];
}

public record ItemRefeicaoDto(int Id, int AlimentoId, string Alimento, decimal Gramas, MacrosDto Macros, decimal? GramasSugeridasIA, decimal? ConfiancaIA, bool EditadoPeloUsuario);

public record RefeicaoDto(int Id, DateTime DataHora, TipoRefeicao Tipo, OrigemRefeicao Origem, string? Descricao, IReadOnlyList<ItemRefeicaoDto> Itens, MacrosDto Total);

// ---------- Favoritos ----------

public class NovoFavoritoItemDto
{
    public int AlimentoId { get; set; }
    [Range(1, 1500)] public decimal Gramas { get; set; }
}

public class NovoFavoritoDto
{
    [Required, StringLength(100)] public string Nome { get; set; } = "";
    public TipoRefeicao? TipoSugerido { get; set; }
    [MinLength(1)] public List<NovoFavoritoItemDto> Itens { get; set; } = [];
}

public record FavoritoItemDto(int AlimentoId, string Alimento, decimal Gramas, MacrosDto Macros);

public record FavoritoDto(int Id, string Nome, TipoRefeicao? TipoSugerido, IReadOnlyList<FavoritoItemDto> Itens, MacrosDto Total);

// ---------- Painel ----------

public record SugestaoFavoritoDto(int FavoritoId, string Nome, MacrosDto Total);

public record AlvoRefeicaoDto(TipoRefeicao Tipo, bool Registrada, MacrosDto AlvoOriginal, MacrosDto Alvo, MacrosDto Consumido, bool NoPiso, IReadOnlyList<SugestaoFavoritoDto> Sugestoes);

public record PainelDiaDto(
    DateOnly Data,
    MacrosDto Meta,
    MacrosDto Consumido,
    MacrosDto Restante,
    IReadOnlyList<AlvoRefeicaoDto> Refeicoes,
    IReadOnlyList<RefeicaoDto> Lancamentos,
    IReadOnlyList<string> Alertas,
    bool DiaDeTreino = false,
    string? AjusteTreino = null);

// ---------- Peso, medidas, progresso ----------

public class NovoPesoDto
{
    public DateOnly? Data { get; set; }
    [Range(30, 300)] public decimal PesoKg { get; set; }
}

public class NovasMedidasDto
{
    public DateOnly? Data { get; set; }
    [Range(40, 200)] public decimal CinturaCm { get; set; }
    [Range(20, 70)] public decimal PescocoCm { get; set; }
    [Range(50, 200)] public decimal? QuadrilCm { get; set; }
    [StringLength(1000)] public string? ObservacaoCorporal { get; set; }
    [Range(3, 60)] public decimal? GorduraFotoMinPercent { get; set; }
    [Range(3, 60)] public decimal? GorduraFotoMaxPercent { get; set; }
}

public record PontoPesoDto(DateOnly Data, decimal? PesoKg, decimal? MediaMovel7d);

public record MedidaDto(int Id, DateOnly Data, decimal CinturaCm, decimal PescocoCm, decimal? QuadrilCm, string? ObservacaoCorporal, decimal? GorduraEstimadaPercent,
    decimal? GorduraFotoMinPercent = null, decimal? GorduraFotoMaxPercent = null);

public record ProgressoDto(IReadOnlyList<PontoPesoDto> Peso, IReadOnlyList<MedidaDto> Medidas, IReadOnlyList<AjusteMetaDto> Ajustes, decimal? ErroMedioIAGramas);

// ---------- Treino ----------

public record AcademiaDto(int Id, string Nome);

public class NovaAcademiaDto
{
    [Required, StringLength(100)] public string Nome { get; set; } = "";
}

public record RascunhoAparelhoDto(string? Exercicio, string? GrupoMuscular, TipoCarga? TipoCarga, IReadOnlyList<string> Alertas, bool Descartado, bool FalhaIA, string? MensagemFalha);

public class NovoAparelhoDto
{
    public int AcademiaId { get; set; }
    [Required, StringLength(100)] public string Nome { get; set; } = "";
    [Required, StringLength(100)] public string Exercicio { get; set; } = "";
    [Required, StringLength(60)] public string GrupoMuscular { get; set; } = "";
    public TipoCarga TipoCarga { get; set; } = TipoCarga.Pilha;
    [Range(0, 50)] public decimal? IncrementoKg { get; set; }
}

public record AparelhoDto(int Id, int AcademiaId, string Academia, string Nome, string Exercicio, string GrupoMuscular, TipoCarga TipoCarga, decimal? IncrementoKg);

public class NovoRotinaItemDto
{
    public int AparelhoId { get; set; }
    [Range(1, 10)] public int SeriesAlvo { get; set; } = 3;
    [Range(1, 50)] public int RepsMin { get; set; } = 8;
    [Range(1, 50)] public int RepsMax { get; set; } = 12;
}

public class NovaRotinaDto
{
    [Required, StringLength(60)] public string Nome { get; set; } = "";
    [MinLength(1)] public List<NovoRotinaItemDto> Itens { get; set; } = [];
}

public record RotinaItemDto(int AparelhoId, string Aparelho, string Exercicio, int Ordem, int SeriesAlvo, int RepsMin, int RepsMax);

public record RotinaDto(int Id, string Nome, IReadOnlyList<RotinaItemDto> Itens);

public class NovaSessaoDto
{
    public int? RotinaId { get; set; }
    [StringLength(500)] public string? Observacao { get; set; }
    /// <summary>Gerado no aparelho: o reenvio da fila offline devolve a mesma sessão.</summary>
    public Guid? IdCliente { get; set; }
    /// <summary>Hora do início no aparelho (sessão iniciada offline); vazio usa a hora do servidor.</summary>
    public DateTime? DataHora { get; set; }
}

public class NovaSerieDto
{
    public int AparelhoId { get; set; }
    [Range(0, 1000)] public decimal CargaKg { get; set; }
    [Range(1, 100)] public int Reps { get; set; }
    [Range(0, 10)] public int? RepsNaReserva { get; set; }
    /// <summary>Gerado no aparelho: o reenvio da fila offline devolve a mesma série.</summary>
    public Guid? IdCliente { get; set; }
}

/// <summary>Id 0 com IdCliente: série ainda na fila do aparelho (modo offline).</summary>
public record SerieDto(int Id, int AparelhoId, int Ordem, decimal CargaKg, int Reps, int? RepsNaReserva, Guid? IdCliente = null)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool Pendente => Id == 0;
}

/// <summary>Séries pré-preenchidas com a última sessão do aparelho.</summary>
public record ExercicioSessaoDto(
    AparelhoDto Aparelho,
    int? SeriesAlvo,
    int? RepsMin,
    int? RepsMax,
    IReadOnlyList<SerieDto> SeriesHoje,
    IReadOnlyList<SerieDto> UltimaSessao,
    decimal? Melhor1RmHoje,
    decimal? Melhor1RmAnterior,
    decimal VolumeHoje,
    decimal VolumeAnterior);

public record SessaoDto(int Id, DateTime DataHora, int? RotinaId, string? Rotina, string? Observacao, IReadOnlyList<ExercicioSessaoDto> Exercicios);

public record SessaoResumoDto(int Id, DateTime DataHora, string? Rotina, int TotalSeries, decimal Volume);

public record PontoEvolucaoDto(DateTime DataHora, decimal Melhor1Rm, decimal Volume, decimal CargaMax, int RepsMax);

public record EvolucaoAparelhoDto(AparelhoDto Aparelho, IReadOnlyList<PontoEvolucaoDto> Sessoes, decimal RecordePessoal1Rm, bool Estagnado, decimal? SugestaoCargaKg);

public record VolumeGrupoDto(string GrupoMuscular, decimal Volume, int Series);

public record EvolucaoSemanaDto(DateOnly De, DateOnly Ate, IReadOnlyList<VolumeGrupoDto> Grupos, IReadOnlyList<string> Alertas);

// ---------- Acesso ----------

public record SessaoUsuarioDto(bool Configurado, bool Autenticado, string? Login);

public class LoginDto
{
    [Required, StringLength(60)] public string Login { get; set; } = "";
    [Required, StringLength(200)] public string Senha { get; set; } = "";
    public bool Lembrar { get; set; } = true;
}

public class ConfigurarAcessoDto
{
    [Required, StringLength(60, MinimumLength = 3)] public string Login { get; set; } = "";
    [Required, StringLength(200, MinimumLength = 10, ErrorMessage = "A senha precisa de pelo menos 10 caracteres.")]
    public string Senha { get; set; } = "";
    /// <summary>Exigido quando o primeiro acesso não vem do próprio computador do servidor.</summary>
    [StringLength(200)] public string? Codigo { get; set; }
}

public class TrocarSenhaDto
{
    [Required, StringLength(200)] public string SenhaAtual { get; set; } = "";
    [Required, StringLength(200, MinimumLength = 10, ErrorMessage = "A senha precisa de pelo menos 10 caracteres.")]
    public string NovaSenha { get; set; } = "";
}

// ---------- Fase 2: cardápio ----------

public record ItemCardapioDto(int AlimentoId, string Alimento, decimal Gramas, MacrosDto Macros);

public record OpcaoCardapioDto(IReadOnlyList<ItemCardapioDto> Itens, MacrosDto Total, MacrosDto Diferenca);

public record CardapioDto(TipoRefeicao Tipo, MacrosDto Alvo, IReadOnlyList<OpcaoCardapioDto> Opcoes);

// ---------- Fase 2: relatórios ----------

public record DiaRelatorioDto(DateOnly Data, MacrosDto Meta, MacrosDto Consumido, bool Registrado, bool DiaDeTreino, decimal? PesoKg);

public record RelatorioDto(
    string Periodo,
    DateOnly De,
    DateOnly Ate,
    IReadOnlyList<DiaRelatorioDto> Dias,
    int DiasRegistrados,
    MacrosDto MediaConsumida,
    MacrosDto MediaMeta,
    int DiasNaMetaKcal,
    int DiasProteinaAtingida,
    decimal? PesoInicioMedia7d,
    decimal? PesoFimMedia7d,
    decimal? VariacaoCinturaCm,
    int SessoesTreino,
    decimal VolumeTreino,
    IReadOnlyList<string> Recordes,
    IReadOnlyList<AjusteMetaDto> Ajustes,
    IReadOnlyList<string> Destaques);

// ---------- Fase 2: avaliação corporal por foto ----------

public record AvaliacaoCorporalDto(
    bool Permitido,
    string? MotivoBloqueio,
    decimal? GorduraMinPercent,
    decimal? GorduraMaxPercent,
    string? Observacoes,
    IReadOnlyList<string> Alertas,
    bool FalhaIA,
    string? MensagemFalha,
    string Provedor);

// ---------- Offline ----------

/// <summary>Alimento com termos normalizados (nome + sinônimos) para a busca offline.</summary>
public record AlimentoCatalogoDto(AlimentoDto Alimento, IReadOnlyList<string> Termos);

public record CatalogoAlimentosDto(string Versao, IReadOnlyList<AlimentoCatalogoDto> Itens);
