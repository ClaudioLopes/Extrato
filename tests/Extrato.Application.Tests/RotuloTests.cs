using System.Net;
using System.Text;
using Extrato.Application.Servicos;
using Extrato.Contracts;
using Extrato.Infrastructure.Imagem;
using Extrato.Infrastructure.Visao;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Extrato.Application.Tests;

/// <summary>Alimento por rótulo: valores por porção convertidos para 100 g e leitura da tabela pela IA.</summary>
public class RotuloTests
{
    [Fact]
    public void Valores_da_porcao_viram_por_100_g()
    {
        // pré-treino: porção de 10 g com 30 kcal e 7 g de carbo
        var dto = Rotulo.ParaCadastro(" Pré-treino Haze ", 10, 30, 0, 7, 0, null, ["pré-treino", "haze"]);

        dto.Nome.Should().Be("Pré-treino Haze");
        dto.Kcal100g.Should().Be(300);
        dto.Carbo100g.Should().Be(70);
        dto.Proteina100g.Should().Be(0);
        dto.Sinonimos.Should().Equal("pré-treino", "haze");
    }

    [Fact]
    public void Porcao_de_100_g_nao_muda_os_numeros() =>
        Rotulo.Por100g(12.3m, 100).Should().Be(12.3m);

    private static RotuloService Servico(IVisionProvider vision) =>
        new(vision, new SanitizadorImagem(), NullLogger<RotuloService>.Instance);

    [Fact]
    public async Task Foto_da_tabela_devolve_os_numeros_da_porcao()
    {
        var r = await Servico(new FakeVisionProvider()).AnalisarAsync(FluxosTests.ImagemComExif(), default);

        r.FalhaIA.Should().BeFalse();
        r.PorcaoG.Should().Be(30);
        r.ProteinaG.Should().Be(24);
        r.Alertas.Should().NotContain(a => a.Contains("não batem"));
    }

    [Fact]
    public async Task IA_fora_do_ar_cai_no_preenchimento_manual()
    {
        var r = await Servico(new VisaoQueFalha()).AnalisarAsync(FluxosTests.ImagemComExif(), default);
        r.FalhaIA.Should().BeTrue();
    }

    private sealed class RotuloComContaErrada : IVisionProvider
    {
        public string Nome => "Teste";
        public bool AceitaDadosSensiveis => false;
        public Task<AnaliseCorpoResultado> AnalisarCorpoAsync(Stream i, string? d, Domain.Sexo s, CancellationToken ct) => throw new NotImplementedException();
        public Task<AnalisePratoResultado> AnalisarPratoAsync(Stream i, string? d, Domain.TipoRefeicao t, CancellationToken ct) => throw new NotImplementedException();
        public Task<AnaliseAparelhoResultado> AnalisarAparelhoAsync(Stream i, string? n, CancellationToken ct) => throw new NotImplementedException();
        // 500 kcal não bate com 10 g de carbo (~40 kcal); porção não encontrada
        public Task<AnaliseRotuloResultado> AnalisarRotuloAsync(Stream i, CancellationToken ct) =>
            Task.FromResult(new AnaliseRotuloResultado(true, null, null, 500, 0, 10, 0, null, []));
    }

    [Fact]
    public async Task Avisa_quando_calorias_nao_batem_ou_falta_a_porcao()
    {
        var r = await Servico(new RotuloComContaErrada()).AnalisarAsync(FluxosTests.ImagemComExif(), default);

        r.PorcaoG.Should().Be(100);
        r.Alertas.Should().Contain(a => a.Contains("não batem")).And.Contain(a => a.Contains("Porção não encontrada"));
    }

    [Fact]
    public async Task Gemini_le_a_tabela_pelo_schema()
    {
        var resposta = """
        {"candidates":[{"content":{"parts":[{"text":"{\"ehTabelaNutricional\":true,\"nome\":\"Haze Melancia\",\"porcaoG\":10,\"kcal\":28,\"proteinaG\":0,\"carboG\":7,\"gorduraG\":0,\"fibraG\":0,\"alertas\":[]}"}]}}]}
        """;
        var http = new HttpClient(new Responde(resposta)) { BaseAddress = new Uri("https://gemini.teste/v1beta/") };
        var g = new GeminiVisionProvider(http, Options.Create(new GeminiOptions { ApiKey = "chave-de-teste", Modelo = "m", ModelosReserva = [] }),
            NullLogger<GeminiVisionProvider>.Instance);

        var r = await g.AnalisarRotuloAsync(new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]), default);

        r.Nome.Should().Be("Haze Melancia");
        r.PorcaoG.Should().Be(10);
        r.Kcal.Should().Be(28);
        r.CarboG.Should().Be(7);
        r.FibraG.Should().BeNull();
    }

    private sealed class Responde(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
