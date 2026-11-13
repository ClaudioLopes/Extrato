using Extrato.Application;
using Extrato.Application.Servicos;
using Extrato.Infrastructure.Dados;
using Extrato.Infrastructure.Imagem;
using Extrato.Infrastructure.Visao;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Extrato.Infrastructure;

public class UsuarioPadrao : IUsuarioAtual
{
    public int UsuarioId => SemeadorDados.UsuarioPadraoId;
}

/// <summary>Relógio no fuso configurado (padrão America/Sao_Paulo): o "dia" do painel é o dia local.</summary>
public class RelogioLocal(IConfiguration config) : IRelogio
{
    private readonly TimeZoneInfo _fuso = ObterFuso(config["Extrato:FusoHorario"] ?? "America/Sao_Paulo");

    public DateTime Agora => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _fuso);

    private static TimeZoneInfo ObterFuso(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("Extrato")
            ?? throw new InvalidOperationException("ConnectionStrings:Extrato não configurada.");
        services.AddDbContext<ExtratoDbContext>(o => o.UseSqlServer(conn, sql => sql.EnableRetryOnFailure(5)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<ExtratoDbContext>());

        services.AddSingleton<IUsuarioAtual, UsuarioPadrao>();
        services.AddSingleton<IRelogio, RelogioLocal>();
        services.AddSingleton<IProcessadorImagem, SanitizadorImagem>();
        services.AddSingleton<Application.Servicos.IHasherSenha, HasherSenhaPbkdf2>();
        services.Configure<Application.Servicos.OpcoesAcesso>(config.GetSection(Application.Servicos.OpcoesAcesso.Secao));

        services.Configure<GeminiOptions>(config.GetSection(GeminiOptions.Secao));
        services.Configure<ClaudeOptions>(config.GetSection(ClaudeOptions.Secao));

        switch (EscolherProvedor(config))
        {
            case "Claude":
                services.AddSingleton<IVisionProvider, ClaudeVisionProvider>();
                break;
            case "Gemini":
                AddGemini(services);
                break;
            default:
                services.AddSingleton<IVisionProvider, FakeVisionProvider>();
                break;
        }

        services.AddHostedService<AjusteSemanalJob>();
        return services;
    }

    /// <summary>
    /// Visao:Provedor = Claude | Gemini | Fake. Vazio: Claude se houver chave da Anthropic
    /// (Claude:ApiKey ou ANTHROPIC_API_KEY), senão Gemini se houver chave, senão Fake.
    /// </summary>
    public static string EscolherProvedor(IConfiguration config)
    {
        var provedor = config["Visao:Provedor"];
        if (!string.IsNullOrWhiteSpace(provedor))
        {
            foreach (var p in new[] { "Claude", "Gemini", "Fake" })
                if (string.Equals(provedor, p, StringComparison.OrdinalIgnoreCase)) return p;
            throw new InvalidOperationException($"Visao:Provedor inválido: '{provedor}'. Use Claude, Gemini ou Fake.");
        }
        if (!string.IsNullOrWhiteSpace(config[$"{ClaudeOptions.Secao}:ApiKey"]) || !string.IsNullOrWhiteSpace(config["ANTHROPIC_API_KEY"]))
            return "Claude";
        if (!string.IsNullOrWhiteSpace(config[$"{GeminiOptions.Secao}:ApiKey"]))
            return "Gemini";
        return "Fake";
    }

    private static void AddGemini(IServiceCollection services)
    {
        {
            services.AddHttpClient<IVisionProvider, GeminiVisionProvider>((sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(opt.TimeoutSegundos * opt.MaxTentativas + 30);
            });
        }
    }

    public static async Task InicializarBancoAsync(this IServiceProvider sp, CancellationToken ct = default)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExtratoDbContext>();
        await db.Database.MigrateAsync(ct);
        await SemeadorDados.SemearAsync(db, scope.ServiceProvider.GetRequiredService<IRelogio>(), ct);
    }
}

/// <summary>Roda o ajuste semanal (seção 6.5) uma vez por dia no dia configurado (padrão segunda-feira).</summary>
public class AjusteSemanalJob(IServiceScopeFactory scopes, IRelogio relogio, IConfiguration config, ILogger<AjusteSemanalJob> logger) : BackgroundService
{
    private DateOnly? _ultimaExecucao;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Extrato:AjusteSemanalAutomatico", true)) return;
        var dia = Enum.TryParse<DayOfWeek>(config["Extrato:DiaAjusteSemanal"], true, out var d) ? d : DayOfWeek.Monday;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            var hoje = relogio.Hoje;
            if (hoje.DayOfWeek == dia && _ultimaExecucao != hoje && relogio.Agora.Hour >= 6)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var r = await scope.ServiceProvider.GetRequiredService<MetaService>().AjusteSemanalAsync(stoppingToken);
                    logger.LogInformation("Ajuste semanal: {Ajustou} — {Motivo}", r.Ajustou, r.Motivo);
                    _ultimaExecucao = hoje;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Ajuste semanal automático falhou.");
                    _ultimaExecucao = hoje;
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
