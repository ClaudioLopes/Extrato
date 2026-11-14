using System.Text;
using Extrato.Application;
using Extrato.Application.Servicos;
using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Infrastructure;
using Extrato.Infrastructure.Dados;
using Extrato.Infrastructure.Imagem;
using Extrato.Infrastructure.Visao;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Extrato.Application.Tests;

public class RelogioFixo(DateTime agora) : IRelogio
{
    public DateTime Agora { get; set; } = agora;
}

public class VisaoQueFalha : IVisionProvider
{
    public string Nome => "falha";
    public bool AceitaDadosSensiveis => true;
    public Task<AnaliseCorpoResultado> AnalisarCorpoAsync(Stream imagem, string? descricao, Sexo sexo, CancellationToken ct) =>
        throw new VisionIndisponivelException("429");
    public Task<AnalisePratoResultado> AnalisarPratoAsync(Stream imagem, string? descricao, TipoRefeicao tipo, CancellationToken ct) =>
        throw new VisionIndisponivelException("Cota do Gemini esgotada (HTTP 429).");
    public Task<AnaliseAparelhoResultado> AnalisarAparelhoAsync(Stream imagem, string? nome, CancellationToken ct) =>
        throw new VisionIndisponivelException("429");
}

public class VisaoComPessoas : IVisionProvider
{
    public string Nome => "pessoas";
    public bool AceitaDadosSensiveis => false;
    public Task<AnaliseCorpoResultado> AnalisarCorpoAsync(Stream imagem, string? descricao, Sexo sexo, CancellationToken ct) =>
        throw new InvalidOperationException("não deveria ser chamado: provedor grátis");
    public Task<AnalisePratoResultado> AnalisarPratoAsync(Stream i, string? d, TipoRefeicao t, CancellationToken ct) =>
        Task.FromResult(new AnalisePratoResultado([new ItemIdentificado("arroz", null, 2000, 0.9m, null)], []));
    public Task<AnaliseAparelhoResultado> AnalisarAparelhoAsync(Stream imagem, string? nome, CancellationToken ct) =>
        Task.FromResult(new AnaliseAparelhoResultado("Supino", "Peito", "Anilha", true, []));
}

/// <summary>Fluxos da Application com EF InMemory + seed real + FakeVisionProvider.</summary>
public class FluxosTests : IAsyncLifetime
{
    private readonly ExtratoDbContext _db;
    private readonly RelogioFixo _relogio = new(new DateTime(2026, 11, 13, 13, 0, 0));
    private readonly UsuarioPadrao _usuario = new();

    public FluxosTests()
    {
        var options = new DbContextOptionsBuilder<ExtratoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new ExtratoDbContext(options);
    }

    public Task InitializeAsync() => SemeadorDados.SemearAsync(_db, _relogio);
    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private AlimentoService Alimentos() => new(_db);

    private RefeicaoService Refeicoes(IVisionProvider? visao = null) =>
        new(_db, _usuario, _relogio, visao ?? new FakeVisionProvider(), new SanitizadorImagem(), Alimentos(), NullLogger<RefeicaoService>.Instance);

    private PainelService Painel()
    {
        var perfil = new PerfilService(_db, _usuario, _relogio);
        var treino = new TreinoService(_db, _usuario, _relogio, new FakeVisionProvider(), new SanitizadorImagem(), NullLogger<TreinoService>.Instance);
        var metas = new MetaService(_db, _usuario, _relogio, perfil, treino);
        var refeicoes = Refeicoes();
        return new PainelService(_relogio, metas, perfil, refeicoes, new FavoritoService(_db, _usuario, refeicoes));
    }

    private const string MarcaExif = "CameraTesteGPS";

    private static byte[] Segmento(byte marcador, byte[] dados)
    {
        var tam = dados.Length + 2;
        return [0xFF, marcador, (byte)(tam >> 8), (byte)(tam & 0xFF), .. dados];
    }

    /// <summary>JPEG sintético: SOI, APP0/JFIF, APP1/EXIF (com marca), COM, DQT, SOF0, SOS, dados, EOI.</summary>
    internal static MemoryStream ImagemComExif(int largura = 1024, int altura = 768)
    {
        byte[] jfif = [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0];
        byte[] exif = [.. "Exif\0\0"u8, 0x49, 0x49, 0x2A, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, .. System.Text.Encoding.ASCII.GetBytes(MarcaExif)];
        byte[] sof = [8, (byte)(altura >> 8), (byte)altura, (byte)(largura >> 8), (byte)largura, 1, 1, 0x11, 0];
        byte[] jpeg =
        [
            0xFF, 0xD8,
            .. Segmento(0xE0, jfif),
            .. Segmento(0xE1, exif),
            .. Segmento(0xFE, System.Text.Encoding.ASCII.GetBytes("comentario")),
            .. Segmento(0xDB, new byte[65]),
            .. Segmento(0xC0, sof),
            .. Segmento(0xDA, [1, 1, 0, 0, 0x3F, 0]),
            0x12, 0x34, 0x56,
            0xFF, 0xD9
        ];
        return new MemoryStream(jpeg);
    }

    [Fact]
    public async Task Sanitizador_remove_EXIF_e_comentarios_e_mantem_a_imagem()
    {
        var entrada = ImagemComExif();
        Encoding.ASCII.GetString(entrada.ToArray()).Should().Contain(MarcaExif);

        await using var saida = await new SanitizadorImagem().PrepararAsync(entrada, default);
        var texto = Encoding.ASCII.GetString(saida.ToArray());
        texto.Should().NotContain(MarcaExif).And.NotContain("Exif").And.NotContain("comentario");
        texto.Should().Contain("JFIF");
        saida.ToArray()[^2..].Should().Equal(0xFF, 0xD9);
    }

    [Fact]
    public async Task Sanitizador_rejeita_resolucao_absurda()
    {
        var act = () => new SanitizadorImagem().PrepararAsync(ImagemComExif(20000, 100), default);
        await act.Should().ThrowAsync<ValidacaoException>().WithMessage("*resolução*");
    }

    [Fact]
    public async Task Sanitizador_remove_chunks_de_texto_do_png()
    {
        static byte[] Chunk(string tipo, byte[] dados) =>
            [(byte)(dados.Length >> 24), (byte)(dados.Length >> 16), (byte)(dados.Length >> 8), (byte)dados.Length,
             .. System.Text.Encoding.ASCII.GetBytes(tipo), .. dados, 0, 0, 0, 0];
        byte[] ihdr = [0, 0, 0, 10, 0, 0, 0, 10, 8, 2, 0, 0, 0];
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", ihdr),
            .. Chunk("tEXt", System.Text.Encoding.ASCII.GetBytes("GPS secreto")), .. Chunk("IDAT", [1, 2, 3]), .. Chunk("IEND", [])];

        await using var saida = await new SanitizadorImagem().PrepararAsync(new MemoryStream(png), default);
        Encoding.ASCII.GetString(saida.ToArray()).Should().NotContain("GPS secreto").And.Contain("IDAT");
    }

    [Fact]
    public async Task Upload_que_nao_e_imagem_vira_erro_de_validacao_e_nao_fallback()
    {
        var act = () => Refeicoes().AnalisarFotoAsync(new MemoryStream(Encoding.UTF8.GetBytes("nao sou imagem")), TipoRefeicao.Almoco, null, default);
        await act.Should().ThrowAsync<ValidacaoException>();
    }

    [Fact]
    public async Task Arquivo_que_nao_e_imagem_e_rejeitado()
    {
        var act = () => new SanitizadorImagem().PrepararAsync(new MemoryStream(Encoding.UTF8.GetBytes("texto")), default);
        await act.Should().ThrowAsync<ValidacaoException>();
    }

    [Fact]
    public async Task Analise_de_foto_devolve_rascunho_com_match_na_TACO_sem_gravar_nada()
    {
        var r = await Refeicoes().AnalisarFotoAsync(ImagemComExif(), TipoRefeicao.Almoco, "2 conchas de arroz, filé de frango ~150 g, feijão", default);

        r.FalhaIA.Should().BeFalse();
        r.Itens.Should().HaveCount(3);
        r.Itens[0].AlimentoNome.Should().Be("Arroz, tipo 1, cozido");
        r.Itens[0].GramasSugeridas.Should().Be(140);
        r.Itens[1].AlimentoNome.Should().Be("Frango, peito, sem pele, grelhado");
        r.Itens[1].Macros!.ProteinaG.Should().Be(48);
        r.Itens[2].BaixaConfianca.Should().BeTrue();
        (await _db.Refeicoes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Item_que_o_usuario_evita_aparece_sinalizado()
    {
        var r = await Refeicoes().AnalisarFotoAsync(ImagemComExif(), TipoRefeicao.Almoco, "200 g de macarrão", default);
        r.Itens.Single().Evitado.Should().BeTrue();
        r.Alertas.Should().Contain(a => a.Contains("evita"));
    }

    [Fact]
    public async Task Item_sem_match_fica_nao_identificado()
    {
        var r = await Refeicoes().AnalisarFotoAsync(ImagemComExif(), TipoRefeicao.Almoco, "100 g de xpto quinoa negra", default);
        r.Itens.Single().NaoIdentificado.Should().BeTrue();
        r.Itens.Single().AlimentoId.Should().BeNull();
    }

    [Fact]
    public async Task Falha_da_IA_cai_no_fallback_manual_com_descricao_preservada()
    {
        var r = await Refeicoes(new VisaoQueFalha()).AnalisarFotoAsync(ImagemComExif(), TipoRefeicao.Noite, "pão com ovo", default);
        r.FalhaIA.Should().BeTrue();
        r.Descricao.Should().Be("pão com ovo");
        r.MensagemFalha.Should().Contain("429");
    }

    [Fact]
    public async Task Json_da_IA_fora_da_faixa_vira_fallback()
    {
        var r = await Refeicoes(new VisaoComPessoas()).AnalisarFotoAsync(ImagemComExif(), TipoRefeicao.Almoco, null, default);
        r.FalhaIA.Should().BeTrue();
    }

    [Fact]
    public async Task Refeicao_confirmada_recalcula_macros_pela_tabela_e_marca_edicao()
    {
        var arroz = await _db.Alimentos.SingleAsync(a => a.Nome == "Arroz, tipo 1, cozido");
        var dto = await Refeicoes().CriarAsync(new NovaRefeicaoDto
        {
            Tipo = TipoRefeicao.Almoco,
            Origem = OrigemRefeicao.Foto,
            Itens = [new NovoItemRefeicaoDto { AlimentoId = arroz.Id, Gramas = 200, GramasSugeridasIA = 150, ConfiancaIA = 0.8m }]
        }, default);

        dto.Total.Kcal.Should().Be(256);
        dto.Itens.Single().EditadoPeloUsuario.Should().BeTrue();
    }

    [Fact]
    public async Task Painel_reflete_consumo_e_redistribui_restante()
    {
        var frango = await _db.Alimentos.SingleAsync(a => a.Nome == "Frango, peito, sem pele, grelhado");
        await Refeicoes().CriarAsync(new NovaRefeicaoDto
        {
            Tipo = TipoRefeicao.Almoco, Itens = [new NovoItemRefeicaoDto { AlimentoId = frango.Id, Gramas = 200 }]
        }, default);

        var p = await Painel().DiaAsync(null, default);

        p.Meta.Kcal.Should().Be(2830);
        p.Consumido.Kcal.Should().Be(318);
        p.Restante.Kcal.Should().Be(2830 - 318);
        p.Refeicoes.Single(r => r.Tipo == TipoRefeicao.Almoco).Registrada.Should().BeTrue();
        p.Refeicoes.Where(r => !r.Registrada).Sum(r => r.Alvo.Kcal).Should().BeApproximately(2830 - 318, 1);
        p.Refeicoes.Single(r => r.Tipo == TipoRefeicao.Tarde).Sugestoes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Painel_alerta_proteina_atrasada_no_fim_da_tarde()
    {
        _relogio.Agora = new DateTime(2026, 11, 13, 19, 0, 0);
        var p = await Painel().DiaAsync(null, default);
        p.Alertas.Should().Contain(a => a.Contains("Proteína atrasada"));
    }

    [Fact]
    public async Task Busca_aproximada_encontra_por_sinonimo_e_sem_acento()
    {
        var r = await Alimentos().BuscarAsync("feijao", 5, default);
        r.First().Nome.Should().StartWith("Feijão");
        (await Alimentos().MatchAsync("whey", null, default))!.Alimento.Nome.Should().Contain("Whey");
    }

    [Fact]
    public async Task Importacao_csv_insere_e_atualiza()
    {
        var csv = "nome;kcal;proteina;carboidrato;gordura;fibra\nTapioca, goma;240;0,2;59,7;0;0,7\nMorango, cru;31;0,9;6,8;0,3;1,7\nlinha;ruim\n";
        var r = await Alimentos().ImportarCsvAsync(new MemoryStream(Encoding.UTF8.GetBytes(csv)), FonteAlimento.Taco, default);
        r.Inseridos.Should().Be(1);
        r.Atualizados.Should().Be(1);
        r.Erros.Should().ContainSingle();
        (await _db.Alimentos.SingleAsync(a => a.Nome == "Morango, cru")).Kcal100g.Should().Be(31);
    }

    [Fact]
    public async Task Alimento_por_rotulo_com_macros_incoerentes_e_rejeitado()
    {
        var act = () => Alimentos().CriarAsync(new NovoAlimentoDto { Nome = "Barra X", Kcal100g = 100, Proteina100g = 30, Carbo100g = 40, Gordura100g = 20 }, default);
        await act.Should().ThrowAsync<ValidacaoException>();
    }

    [Fact]
    public async Task Aparelho_com_pessoas_no_enquadramento_e_descartado()
    {
        var treino = new TreinoService(_db, _usuario, _relogio, new VisaoComPessoas(), new SanitizadorImagem(), NullLogger<TreinoService>.Instance);
        var r = await treino.AnalisarFotoAparelhoAsync(ImagemComExif(), null, default);
        r.Descartado.Should().BeTrue();
        r.Exercicio.Should().BeNull();
    }

    [Fact]
    public async Task Sessao_pre_preenche_com_a_ultima_e_compara()
    {
        var treino = new TreinoService(_db, _usuario, _relogio, new FakeVisionProvider(), new SanitizadorImagem(), NullLogger<TreinoService>.Instance);
        var academia = await _db.Academias.FirstAsync();
        var ap = await treino.CriarAparelhoAsync(new NovoAparelhoDto { AcademiaId = academia.Id, Nome = "Leg", Exercicio = "Leg press", GrupoMuscular = "Pernas" }, default);

        var s1 = await treino.IniciarSessaoAsync(new NovaSessaoDto(), default);
        await treino.RegistrarSerieAsync(s1.Id, new NovaSerieDto { AparelhoId = ap.Id, CargaKg = 100, Reps = 10 }, default);

        _relogio.Agora = _relogio.Agora.AddDays(2);
        var s2 = await treino.IniciarSessaoAsync(new NovaSessaoDto(), default);
        await treino.RegistrarSerieAsync(s2.Id, new NovaSerieDto { AparelhoId = ap.Id, CargaKg = 110, Reps = 10 }, default);

        var sessao = await treino.SessaoAsync(s2.Id, default);
        var ex = sessao.Exercicios.Single();
        ex.UltimaSessao.Single().CargaKg.Should().Be(100);
        ex.Melhor1RmHoje.Should().BeGreaterThan(ex.Melhor1RmAnterior!.Value);

        var evolucao = await treino.EvolucaoAsync(ap.Id, default);
        evolucao.Sessoes.Should().HaveCount(2);
        evolucao.RecordePessoal1Rm.Should().Be(MetricasTreinoEpley(110, 10));
    }

    private static decimal MetricasTreinoEpley(decimal c, int r) => Domain.Regras.MetricasTreino.Epley(c, r);
}
