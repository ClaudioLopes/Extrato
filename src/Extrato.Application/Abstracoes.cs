using Extrato.Domain;
using Extrato.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Extrato.Application;

public interface IAppDbContext
{
    DbSet<Usuario> Usuarios { get; }
    DbSet<ConfiguracaoRegras> Regras { get; }
    DbSet<MetaDiaria> Metas { get; }
    DbSet<AjusteMeta> AjustesMeta { get; }
    DbSet<Alimento> Alimentos { get; }
    DbSet<AlimentoSinonimo> AlimentoSinonimos { get; }
    DbSet<AlimentoEvitado> AlimentosEvitados { get; }
    DbSet<PorcaoFavorita> Favoritos { get; }
    DbSet<Refeicao> Refeicoes { get; }
    DbSet<ItemRefeicao> ItensRefeicao { get; }
    DbSet<RegistroPeso> Pesos { get; }
    DbSet<Medidas> Medidas { get; }
    DbSet<Academia> Academias { get; }
    DbSet<Aparelho> Aparelhos { get; }
    DbSet<Rotina> Rotinas { get; }
    DbSet<SessaoTreino> Sessoes { get; }
    DbSet<Serie> Series { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>App pessoal: um único usuário. A abstração deixa o caminho aberto.</summary>
public interface IUsuarioAtual
{
    int UsuarioId { get; }
}

public interface IRelogio
{
    DateTime Agora { get; }
    DateOnly Hoje => DateOnly.FromDateTime(Agora);
}

// ---------- IA de visão (seção 9) ----------

public record ItemIdentificado(string Nome, string? Preparo, decimal Gramas, decimal Confianca, string? Observacao);

public record AnalisePratoResultado(IReadOnlyList<ItemIdentificado> Itens, IReadOnlyList<string> Alertas);

public record AnaliseAparelhoResultado(string? Exercicio, string? GrupoMuscular, string? TipoCarga, bool HaPessoas, IReadOnlyList<string> Alertas);

public interface IVisionProvider
{
    string Nome { get; }

    Task<AnalisePratoResultado> AnalisarPratoAsync(
        Stream imagem, string? descricao, TipoRefeicao tipo, CancellationToken ct);

    Task<AnaliseAparelhoResultado> AnalisarAparelhoAsync(
        Stream imagem, string? nome, CancellationToken ct);
}

/// <summary>Falha recuperável da IA (429 esgotado, timeout, JSON inválido): o app cai no registro manual.</summary>
public class VisionIndisponivelException(string mensagem, Exception? inner = null) : Exception(mensagem, inner);

/// <summary>Valida e remove metadados (EXIF/GPS) da imagem em memória. Nunca grava em disco. O redimensionamento para 1024 px é feito no cliente.</summary>
public interface IProcessadorImagem
{
    Task<MemoryStream> PrepararAsync(Stream entrada, CancellationToken ct);
}

// ---------- Erros de aplicação ----------

public class NaoEncontradoException(string mensagem) : Exception(mensagem);

public class ValidacaoException(string mensagem) : Exception(mensagem);
