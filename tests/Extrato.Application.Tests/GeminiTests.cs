using System.Net;
using System.Text;
using Extrato.Domain;
using Extrato.Infrastructure.Visao;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Extrato.Application.Tests;

/// <summary>Gemini: modelo reserva quando o principal está sobrecarregado ou não existe, e leitura da resposta.</summary>
public class GeminiTests
{
    /// <summary>Servidor falso: responde por modelo, na ordem da fila de cada um; registra os modelos chamados.</summary>
    private sealed class GoogleFalso(Dictionary<string, Queue<HttpResponseMessage>> respostas) : HttpMessageHandler
    {
        public List<string> Chamados { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var modelo = request.RequestUri!.AbsolutePath.Split('/').Last().Split(':')[0];
            Chamados.Add(modelo);
            return Task.FromResult(respostas[modelo].Dequeue());
        }
    }

    private static HttpResponseMessage Status(HttpStatusCode s) =>
        new(s) { Content = new StringContent("{\"error\":{\"message\":\"The model is overloaded.\"}}", Encoding.UTF8, "application/json") };

    /// <summary>Resposta do Gemini com uma parte de pensamento antes do JSON do aparelho.</summary>
    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""
        {"candidates":[{"content":{"parts":[
          {"text":"pensando no aparelho...","thought":true},
          {"text":"{\"exercicio\":\"Supino reto\",\"grupoMuscular\":\"Peito\",\"tipoCarga\":\"Anilha\",\"haPessoas\":false,\"alertas\":[]}"}
        ]}}]}
        """, Encoding.UTF8, "application/json")
    };

    private static (GeminiVisionProvider, GoogleFalso) Provedor(Dictionary<string, Queue<HttpResponseMessage>> respostas)
    {
        var falso = new GoogleFalso(respostas);
        var http = new HttpClient(falso) { BaseAddress = new Uri("https://gemini.teste/v1beta/") };
        var opt = Options.Create(new GeminiOptions
        {
            ApiKey = "chave-de-teste", Modelo = "principal", ModelosReserva = ["reserva"], MaxTentativas = 2
        });
        return (new GeminiVisionProvider(http, opt, NullLogger<GeminiVisionProvider>.Instance), falso);
    }

    private static MemoryStream Png() => new([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);

    [Fact]
    public async Task Principal_sobrecarregado_passa_para_o_reserva()
    {
        var (g, falso) = Provedor(new()
        {
            ["principal"] = new([Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.ServiceUnavailable)]),
            ["reserva"] = new([Ok()])
        });

        var r = await g.AnalisarAparelhoAsync(Png(), null, default);

        r.Exercicio.Should().Be("Supino reto", "a parte de pensamento é ignorada");
        falso.Chamados.Should().Equal("principal", "principal", "reserva");
    }

    [Fact]
    public async Task Modelo_inexistente_passa_direto_para_o_reserva()
    {
        var (g, falso) = Provedor(new()
        {
            ["principal"] = new([Status(HttpStatusCode.NotFound)]),
            ["reserva"] = new([Ok()])
        });

        (await g.AnalisarAparelhoAsync(Png(), null, default)).GrupoMuscular.Should().Be("Peito");
        falso.Chamados.Should().Equal("principal", "reserva");
    }

    [Fact]
    public async Task Cota_esgotada_nao_troca_de_modelo()
    {
        var (g, falso) = Provedor(new()
        {
            ["principal"] = new([Status(HttpStatusCode.TooManyRequests), Status(HttpStatusCode.TooManyRequests)]),
            ["reserva"] = new([Ok()])
        });

        var acao = () => g.AnalisarAparelhoAsync(Png(), null, default);

        (await acao.Should().ThrowAsync<VisionIndisponivelException>()).Which.Message.Should().Contain("429");
        falso.Chamados.Should().Equal("principal", "principal");
    }

    [Fact]
    public async Task Todos_sobrecarregados_avisa_para_tentar_depois()
    {
        var (g, _) = Provedor(new()
        {
            ["principal"] = new([Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.ServiceUnavailable)]),
            ["reserva"] = new([Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.ServiceUnavailable)])
        });

        var acao = () => g.AnalisarAparelhoAsync(Png(), null, default);

        (await acao.Should().ThrowAsync<VisionIndisponivelException>()).Which.Message.Should().Contain("sobrecarregado");
    }
}
