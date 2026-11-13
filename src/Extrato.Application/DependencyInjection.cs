using Extrato.Application.Servicos;
using Microsoft.Extensions.DependencyInjection;

namespace Extrato.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<PerfilService>();
        services.AddScoped<MetaService>();
        services.AddScoped<AlimentoService>();
        services.AddScoped<RefeicaoService>();
        services.AddScoped<FavoritoService>();
        services.AddScoped<PainelService>();
        services.AddScoped<ProgressoService>();
        services.AddScoped<TreinoService>();
        services.AddScoped<AutenticacaoService>();
        services.AddScoped<CardapioService>();
        services.AddScoped<RelatorioService>();
        services.AddScoped<AvaliacaoCorporalService>();
        return services;
    }
}
