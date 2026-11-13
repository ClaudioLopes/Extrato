using System.Text.Json;
using Extrato.Application;
using Extrato.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Extrato.Infrastructure.Dados;

public class ExtratoDbContext(DbContextOptions<ExtratoDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<ConfiguracaoRegras> Regras => Set<ConfiguracaoRegras>();
    public DbSet<MetaDiaria> Metas => Set<MetaDiaria>();
    public DbSet<AjusteMeta> AjustesMeta => Set<AjusteMeta>();
    public DbSet<Alimento> Alimentos => Set<Alimento>();
    public DbSet<AlimentoSinonimo> AlimentoSinonimos => Set<AlimentoSinonimo>();
    public DbSet<AlimentoEvitado> AlimentosEvitados => Set<AlimentoEvitado>();
    public DbSet<PorcaoFavorita> Favoritos => Set<PorcaoFavorita>();
    public DbSet<Refeicao> Refeicoes => Set<Refeicao>();
    public DbSet<ItemRefeicao> ItensRefeicao => Set<ItemRefeicao>();
    public DbSet<RegistroPeso> Pesos => Set<RegistroPeso>();
    public DbSet<Medidas> Medidas => Set<Medidas>();
    public DbSet<Academia> Academias => Set<Academia>();
    public DbSet<Aparelho> Aparelhos => Set<Aparelho>();
    public DbSet<Rotina> Rotinas => Set<Rotina>();
    public DbSet<SessaoTreino> Sessoes => Set<SessaoTreino>();
    public DbSet<Serie> Series => Set<Serie>();
    public DbSet<Credencial> Credenciais => Set<Credencial>();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HavePrecision(9, 2);
        b.Properties<string>().HaveMaxLength(200);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.ToTable("Usuario");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Nome).HasMaxLength(80);
            e.HasOne(x => x.Regras).WithOne().HasForeignKey<ConfiguracaoRegras>(x => x.UsuarioId);
            e.HasMany(x => x.AlimentosEvitados).WithOne().HasForeignKey(x => x.UsuarioId);
        });

        b.Entity<ConfiguracaoRegras>(e =>
        {
            e.ToTable("ConfiguracaoRegras");
            e.HasKey(x => x.UsuarioId);
            e.Property(x => x.ProteinaGPorKg).HasPrecision(4, 2);
            e.Property(x => x.GorduraGPorKg).HasPrecision(4, 2);
            e.Property(x => x.FatorAtividade).HasPrecision(5, 3);
            e.Property(x => x.PisoRefeicao).HasPrecision(4, 2);
            // PesosRefeicao e Travas em JSON (nvarchar), como no modelo da doc
            e.Property(x => x.DiasTreino).HasMaxLength(40).HasConversion(
                v => string.Join(",", v.Select(d => (int)d)),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => (DayOfWeek)int.Parse(x)).ToList(),
                new ValueComparer<List<DayOfWeek>>((a, c) => a!.SequenceEqual(c!), v => v.Aggregate(0, (h, d) => h * 7 + (int)d), v => v.ToList()));
            e.Property(x => x.PesosRefeicao).HasMaxLength(400).HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<PesosRefeicao>(v, Json) ?? PesosRefeicao.Padrao,
                new ValueComparer<PesosRefeicao>((a, c) => a == c, v => v.GetHashCode(), v => v));
            e.Property(x => x.Travas).HasMaxLength(400).HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<Travas>(v, Json) ?? new Travas(),
                new ValueComparer<Travas>((a, c) => a == c, v => v.GetHashCode(), v => v));
        });

        b.Entity<MetaDiaria>(e =>
        {
            e.ToTable("MetaDiaria");
            e.HasIndex(x => new { x.UsuarioId, x.VigenteDesde });
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId);
        });

        b.Entity<AjusteMeta>(e =>
        {
            e.ToTable("AjusteMeta");
            e.Property(x => x.Motivo).HasMaxLength(1000);
            e.HasIndex(x => new { x.UsuarioId, x.Data });
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId);
        });

        b.Entity<Alimento>(e =>
        {
            e.ToTable("Alimento");
            e.Property(x => x.Nome).HasMaxLength(150);
            e.Property(x => x.NomeNormalizado).HasMaxLength(150);
            e.HasIndex(x => x.NomeNormalizado).IsUnique();
            e.Property(x => x.Kcal100g).HasPrecision(7, 2);
            e.HasMany(x => x.Sinonimos).WithOne().HasForeignKey(x => x.AlimentoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AlimentoSinonimo>(e =>
        {
            e.ToTable("AlimentoSinonimo");
            e.HasKey(x => new { x.AlimentoId, x.Termo });
            e.Property(x => x.Termo).HasMaxLength(150);
            e.HasIndex(x => x.Termo);
        });

        b.Entity<AlimentoEvitado>(e =>
        {
            e.ToTable("AlimentoEvitado");
            e.Property(x => x.Termo).HasMaxLength(100);
            e.HasOne<Alimento>().WithMany().HasForeignKey(x => x.AlimentoId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PorcaoFavorita>(e =>
        {
            e.ToTable("PorcaoFavorita");
            e.Property(x => x.Nome).HasMaxLength(100);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId);
            e.HasMany(x => x.Itens).WithOne().HasForeignKey(x => x.PorcaoFavoritaId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PorcaoFavoritaItem>(e =>
        {
            e.ToTable("PorcaoFavoritaItem");
            e.HasOne(x => x.Alimento).WithMany().HasForeignKey(x => x.AlimentoId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Refeicao>(e =>
        {
            e.ToTable("Refeicao");
            e.Property(x => x.DescricaoUsuario).HasMaxLength(500);
            e.HasIndex(x => new { x.UsuarioId, x.DataHora });
            e.HasIndex(x => new { x.UsuarioId, x.IdCliente }).IsUnique().HasFilter("[IdCliente] IS NOT NULL");
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId);
            e.HasMany(x => x.Itens).WithOne().HasForeignKey(x => x.RefeicaoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ItemRefeicao>(e =>
        {
            e.ToTable("ItemRefeicao");
            e.Property(x => x.Gramas).HasPrecision(7, 1);
            e.Property(x => x.GramasSugeridasIA).HasPrecision(7, 1);
            e.Property(x => x.ConfiancaIA).HasPrecision(3, 2);
            e.HasOne(x => x.Alimento).WithMany().HasForeignKey(x => x.AlimentoId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RegistroPeso>(e =>
        {
            e.ToTable("RegistroPeso");
            e.Property(x => x.PesoKg).HasPrecision(5, 2);
            e.HasIndex(x => new { x.UsuarioId, x.Data }).IsUnique();
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId);
        });

        b.Entity<Medidas>(e =>
        {
            e.ToTable("Medidas");
            e.Property(x => x.ObservacaoCorporal).HasMaxLength(1000);
            e.Property(x => x.GorduraFotoMinPercent).HasPrecision(4, 1);
            e.Property(x => x.GorduraFotoMaxPercent).HasPrecision(4, 1);
            e.HasIndex(x => new { x.UsuarioId, x.Data });
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId);
        });

        b.Entity<Credencial>(e =>
        {
            e.ToTable("Credencial");
            e.HasKey(x => x.UsuarioId);
            e.Property(x => x.Login).HasMaxLength(60);
            e.Property(x => x.SenhaHash).HasMaxLength(400);
            e.HasIndex(x => x.Login).IsUnique();
            e.HasOne<Usuario>().WithOne().HasForeignKey<Credencial>(x => x.UsuarioId);
        });

        b.Entity<Academia>(e => { e.ToTable("Academia"); e.Property(x => x.Nome).HasMaxLength(100); });

        b.Entity<Aparelho>(e =>
        {
            e.ToTable("Aparelho");
            e.Property(x => x.Nome).HasMaxLength(100);
            e.Property(x => x.Exercicio).HasMaxLength(100);
            e.Property(x => x.GrupoMuscular).HasMaxLength(60);
            e.HasOne(x => x.Academia).WithMany().HasForeignKey(x => x.AcademiaId);
        });

        b.Entity<Rotina>(e =>
        {
            e.ToTable("Rotina");
            e.Property(x => x.Nome).HasMaxLength(60);
            e.HasMany(x => x.Itens).WithOne().HasForeignKey(x => x.RotinaId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RotinaItem>(e =>
        {
            e.ToTable("RotinaItem");
            e.HasOne(x => x.Aparelho).WithMany().HasForeignKey(x => x.AparelhoId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SessaoTreino>(e =>
        {
            e.ToTable("SessaoTreino");
            e.Property(x => x.Observacao).HasMaxLength(500);
            e.HasIndex(x => x.DataHora);
            e.HasOne(x => x.Rotina).WithMany().HasForeignKey(x => x.RotinaId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Series).WithOne(x => x.Sessao).HasForeignKey(x => x.SessaoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Serie>(e =>
        {
            e.ToTable("Serie");
            e.Property(x => x.CargaKg).HasPrecision(6, 2);
            e.HasOne(x => x.Aparelho).WithMany().HasForeignKey(x => x.AparelhoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AparelhoId, x.SessaoId });
        });
    }
}
