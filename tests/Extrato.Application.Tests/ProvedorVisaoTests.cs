using Extrato.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Extrato.Application.Tests;

public class ProvedorVisaoTests
{
    private static IConfiguration Config(params (string Chave, string Valor)[] valores) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Chave, v.Valor)))
            .Build();

    [Fact]
    public void Sem_chave_usa_o_Fake() => Extrato.Infrastructure.DependencyInjection.EscolherProvedor(Config()).Should().Be("Fake");

    [Fact]
    public void Chave_da_Anthropic_tem_prioridade_sobre_a_do_Gemini() =>
        Extrato.Infrastructure.DependencyInjection.EscolherProvedor(Config(("Claude:ApiKey", "x"), ("Gemini:ApiKey", "y"))).Should().Be("Claude");

    [Fact]
    public void Variavel_ANTHROPIC_API_KEY_ativa_o_Claude() =>
        Extrato.Infrastructure.DependencyInjection.EscolherProvedor(Config(("ANTHROPIC_API_KEY", "x"))).Should().Be("Claude");

    [Fact]
    public void So_chave_do_Gemini_usa_o_Gemini() =>
        Extrato.Infrastructure.DependencyInjection.EscolherProvedor(Config(("Gemini:ApiKey", "y"))).Should().Be("Gemini");

    [Fact]
    public void Provedor_explicito_vence_e_ignora_maiusculas() =>
        Extrato.Infrastructure.DependencyInjection.EscolherProvedor(Config(("Visao:Provedor", "fake"), ("Claude:ApiKey", "x"))).Should().Be("Fake");

    [Fact]
    public void Provedor_invalido_falha_na_inicializacao()
    {
        var act = () => Extrato.Infrastructure.DependencyInjection.EscolherProvedor(Config(("Visao:Provedor", "OpenAI")));
        act.Should().Throw<InvalidOperationException>();
    }
}
