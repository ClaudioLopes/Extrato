using Extrato.Application.Servicos;
using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Domain.Regras;
using Extrato.Infrastructure;
using Extrato.Infrastructure.Dados;
using Extrato.Infrastructure.Imagem;
using Extrato.Infrastructure.Visao;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Extrato.Application.Tests;

/// <summary>Print da pulseira com só as calorias totais, sem as ativas.</summary>
public class VisaoSoTotais : IVisionProvider
{
    public string Nome => "teste";
    public bool AceitaDadosSensiveis => true;
    public Task<AnaliseCorpoResultado> AnalisarCorpoAsync(Stream imagem, string? descricao, Sexo sexo, CancellationToken ct) => throw new NotImplementedException();
    public Task<AnalisePratoResultado> AnalisarPratoAsync(Stream imagem, string? descricao, TipoRefeicao tipo, CancellationToken ct) => throw new NotImplementedException();
    public Task<AnaliseAparelhoResultado> AnalisarAparelhoAsync(Stream imagem, string? nome, CancellationToken ct) => throw new NotImplementedException();
    public Task<AnaliseAtividadeResultado> AnalisarAtividadeAsync(Stream imagem, CancellationToken ct) =>
        Task.FromResult(new AnaliseAtividadeResultado(EhResumo, "Volei", null, null, 90 * 60, null, 600, null, 128, []));
    public bool EhResumo { get; init; } = true;
}

/// <summary>Atividades (natação, vôlei…): leitura do print, registro e efeito na meta do dia e no ajuste semanal.</summary>
public class AtividadesTests : IAsyncLifetime
{
    private readonly ExtratoDbContext _db = new(new DbContextOptionsBuilder<ExtratoDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    // logo depois da natação do print (13/11 20:06): a meta do seed vale a partir deste dia
    private readonly RelogioFixo _relogio = new(new DateTime(2026, 11, 13, 21, 0, 0));
    private readonly UsuarioPadrao _usuario = new();

    public Task InitializeAsync() => SemeadorDados.SemearAsync(_db, _relogio);
    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private PerfilService Perfil() => new(_db, _usuario, _relogio);

    private AtividadeService Atividades(IVisionProvider? visao = null) =>
        new(_db, _usuario, _relogio, visao ?? new FakeVisionProvider(), new SanitizadorImagem(), Perfil(), NullLogger<AtividadeService>.Instance);

    private MetaService Metas() => new(_db, _usuario, _relogio, Perfil(),
        new TreinoService(_db, _usuario, _relogio, new FakeVisionProvider(), new SanitizadorImagem(), NullLogger<TreinoService>.Instance));

    /// <summary>A natação do print do usuário: 13/11 20:06, 50:47, 312 kcal ativas, 382 totais.</summary>
    private static NovaAtividadeDto Natacao() => new()
    {
        DataHora = new DateTime(2026, 11, 13, 20, 6, 0), Tipo = TipoAtividade.Natacao, DuracaoMin = 51,
        KcalAtivas = 312, KcalTotais = 382, DistanciaM = 1300, FcMedia = 139, Origem = OrigemAtividade.Print
    };

    [Fact]
    public async Task Print_vira_rascunho_com_as_calorias_ativas_sem_gravar_nada()
    {
        var r = await Atividades().AnalisarPrintAsync(FluxosTests.ImagemComExif(), default);

        r.Reconhecido.Should().BeTrue();
        r.FalhaIA.Should().BeFalse();
        r.Atividade.Tipo.Should().Be(TipoAtividade.Natacao);
        r.Atividade.DuracaoMin.Should().Be(51, "50:47 arredonda para 51 min");
        r.Atividade.KcalAtivas.Should().Be(312);
        r.Atividade.KcalTotais.Should().Be(382);
        r.Atividade.Origem.Should().Be(OrigemAtividade.Print);
        (await _db.Atividades.CountAsync()).Should().Be(0, "só grava quando o usuário confirma");
    }

    [Fact]
    public async Task Print_sem_calorias_ativas_estima_pelas_totais_menos_o_repouso()
    {
        var r = await Atividades(new VisaoSoTotais()).AnalisarPrintAsync(FluxosTests.ImagemComExif(), default);

        var p = await Perfil().PerfilCalculoAsync(default);
        var tmb = CalculadoraMetas.Tmb(p.Sexo, p.PesoKg, p.AlturaCm, p.Idade);
        r.Atividade.KcalAtivas.Should().Be(GastoAtividades.AtivasDasTotais(600, 90, tmb)).And.BeLessThan(600);
        r.Atividade.DataHora.Should().Be(_relogio.Agora, "sem data no print, usa agora");
        r.Alertas.Should().Contain(a => a.Contains("totais menos o repouso")).And.Contain(a => a.Contains("usei agora"));
    }

    [Fact]
    public async Task Imagem_que_nao_e_resumo_de_atividade_vem_marcada()
    {
        var r = await Atividades(new VisaoSoTotais { EhResumo = false }).AnalisarPrintAsync(FluxosTests.ImagemComExif(), default);
        r.Reconhecido.Should().BeFalse();
        r.Alertas[0].Should().StartWith("Não parece o resumo");
    }

    [Fact]
    public async Task Provedor_sem_leitura_de_print_cai_no_preenchimento_manual()
    {
        var r = await Atividades(new VisaoQueFalha()).AnalisarPrintAsync(FluxosTests.ImagemComExif(), default);
        r.FalhaIA.Should().BeTrue();
        r.MensagemFalha.Should().NotBeNullOrEmpty();
        r.Atividade.DataHora.Should().Be(_relogio.Agora);
    }

    [Fact]
    public async Task O_mesmo_print_salvo_duas_vezes_nao_duplica()
    {
        var a = await Atividades().CriarAsync(Natacao(), default);
        var b = await Atividades().CriarAsync(Natacao(), default);

        b.Id.Should().Be(a.Id);
        (await _db.Atividades.CountAsync()).Should().Be(1);
        a.KcalNaMeta.Should().Be(234, "75% de 312");
    }

    [Fact]
    public async Task Sem_calorias_estima_pela_tabela_MET_com_o_peso_atual()
    {
        var a = await Atividades().CriarAsync(new NovaAtividadeDto
        {
            DataHora = _relogio.Agora.AddHours(-2), Tipo = TipoAtividade.Volei, DuracaoMin = 60
        }, default);

        a.KcalAtivas.Should().Be(216, "(4 MET − 1) × 72 kg × 1 h");
    }

    [Fact]
    public async Task Atividade_no_futuro_e_recusada()
    {
        var dto = Natacao();
        dto.DataHora = _relogio.Agora.AddHours(3);
        await FluentActions.Awaiting(() => Atividades().CriarAsync(dto, default)).Should().ThrowAsync<ValidacaoException>();
    }

    [Fact]
    public async Task Meta_do_dia_ganha_75_por_cento_das_ativas_em_carboidrato()
    {
        var dia = new DateOnly(2026, 11, 13);
        var antes = (await Metas().MetaDoDiaAsync(dia, default))!;

        await Atividades().CriarAsync(Natacao(), default);
        var depois = (await Metas().MetaDoDiaAsync(dia, default))!;

        depois.Macros.Kcal.Should().Be(antes.Macros.Kcal + 234);
        depois.Macros.CarboG.Should().Be(Math.Round(antes.Macros.CarboG + 234 / Macros.KcalPorGCarbo, 1));
        depois.Macros.ProteinaG.Should().Be(antes.Macros.ProteinaG);
        depois.Explicacao.Should().Contain("Natação: +234 kcal");
        (await Metas().MetaDoDiaAsync(dia.AddDays(1), default))!.Macros.Kcal.Should().Be(antes.Macros.Kcal, "só no dia da atividade");
    }

    [Fact]
    public async Task Com_a_opcao_desligada_a_atividade_nao_muda_a_meta()
    {
        var regras = await Perfil().ObterRegrasAsync(default);
        regras.AtividadesNaMeta = false;
        await _db.SaveChangesAsync();
        var dia = new DateOnly(2026, 11, 13);
        var antes = (await Metas().MetaDoDiaAsync(dia, default))!;

        var a = await Atividades().CriarAsync(Natacao(), default);

        a.KcalNaMeta.Should().Be(0);
        (await Metas().MetaDoDiaAsync(dia, default))!.Macros.Kcal.Should().Be(antes.Macros.Kcal);
    }

    [Fact]
    public void Ajuste_semanal_desconta_as_atividades_ao_estimar_o_GET_base()
    {
        var hoje = new DateOnly(2026, 11, 14);
        var pesos = Enumerable.Range(0, 21).Select(i => (hoje.AddDays(-i), 72m)).ToList();
        var ingestao = Enumerable.Range(0, 7).ToDictionary(i => hoje.AddDays(-i), _ => 2800m);
        EntradaAjusteSemanal Entrada(IReadOnlyDictionary<DateOnly, int>? atividades) =>
            new(hoje, Objetivo.Recomposicao, 2800, 1700, pesos, ingestao, null, new Travas(), null, atividades);

        var sem = AjusteSemanal.Avaliar(Entrada(null));
        // natação (234) e vôlei (162) creditados em 2 dias da semana: comeu essas calorias "de volta"
        var com = AjusteSemanal.Avaliar(Entrada(new Dictionary<DateOnly, int> { [hoje.AddDays(-1)] = 234, [hoje.AddDays(-3)] = 162 }));

        sem.GetEstimado.Should().Be(2800, "peso estável: gasto = ingestão");
        com.GetEstimado.Should().Be(2800 - (int)Math.Round((234 + 162) / 7m), "o gasto base não inclui as atividades");
        com.Motivo.Should().Contain("descontadas");
    }

    [Theory]
    [InlineData(new[] { TipoAtividade.Natacao }, "Natação")]
    [InlineData(new[] { TipoAtividade.Natacao, TipoAtividade.Volei }, "Natação e vôlei")]
    [InlineData(new[] { TipoAtividade.Natacao, TipoAtividade.Volei, TipoAtividade.Natacao, TipoAtividade.Corrida }, "Natação, vôlei e corrida")]
    public void Descricao_junta_os_tipos_do_dia(TipoAtividade[] tipos, string esperado) =>
        GastoAtividades.Descrever(tipos).Should().Be(esperado);
}
