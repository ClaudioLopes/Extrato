using System.Text;
using Extrato.Application.Servicos;
using Extrato.Contracts;
using Extrato.Domain;
using Extrato.Domain.Entidades;
using Extrato.Infrastructure;
using Extrato.Infrastructure.Dados;
using Extrato.Infrastructure.Imagem;
using Extrato.Infrastructure.Visao;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Extrato.Application.Tests;

public class Fase2FluxosTests : IAsyncLifetime
{
    private readonly ExtratoDbContext _db = BancoTeste.Criar();
    // sexta-feira, 13h
    private readonly RelogioFixo _relogio = new(new DateTime(2026, 11, 13, 13, 0, 0));
    private readonly UsuarioPadrao _usuario = new();

    public Task InitializeAsync() => SemeadorDados.SemearAsync(_db, _relogio);
    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private PerfilService Perfil() => new(_db, _usuario, _relogio);
    private TreinoService Treino() => new(_db, _usuario, _relogio, new FakeVisionProvider(), new SanitizadorImagem(), NullLogger<TreinoService>.Instance);
    private MetaService Metas() => new(_db, _usuario, _relogio, Perfil(), Treino());
    private RefeicaoService Refeicoes() => new(_db, _usuario, _relogio, new FakeVisionProvider(), new SanitizadorImagem(), new AlimentoService(_db), NullLogger<RefeicaoService>.Instance);
    private PainelService Painel() => new(_relogio, Metas(), Perfil(), Refeicoes(), new FavoritoService(_db, _usuario, Refeicoes()));

    private static MemoryStream Jpeg() => new([0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x10, 0x00, 0x10, 0x01, 0x01, 0x11, 0x00,
        0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0x12, 0x34, 0xFF, 0xD9]);

    [Fact]
    public async Task Painel_aplica_meta_de_dia_de_treino()
    {
        var regras = await Perfil().ObterRegrasAsync(default);
        regras.CicloTreinoAtivo = true;
        regras.KcalExtraDiaTreino = 200;
        await _db.SaveChangesAsync();

        var p = await Painel().DiaAsync(null, default); // sexta = dia planejado
        p.DiaDeTreino.Should().BeTrue();
        p.Meta.Kcal.Should().Be(2830 + 200);
        p.AjusteTreino.Should().Contain("+200");

        var sabado = await Painel().DiaAsync(new DateOnly(2026, 11, 14), default);
        sabado.DiaDeTreino.Should().BeFalse();
        sabado.Meta.Kcal.Should().Be(2830 - 500);
    }

    [Fact]
    public async Task Cardapio_respeita_evitados_e_usa_a_rotina()
    {
        var frango = await _db.Alimentos.SingleAsync(a => a.Nome == "Frango, peito, sem pele, grelhado");
        var arroz = await _db.Alimentos.SingleAsync(a => a.Nome == "Arroz, tipo 1, cozido");
        for (var i = 1; i <= 3; i++)
            await Refeicoes().CriarAsync(new NovaRefeicaoDto
            {
                DataHora = _relogio.Agora.AddDays(-i), Tipo = TipoRefeicao.Almoco,
                Itens = [new() { AlimentoId = frango.Id, Gramas = 150 }, new() { AlimentoId = arroz.Id, Gramas = 200 }]
            }, default);

        var servico = new CardapioService(_db, _usuario, _relogio, Painel());
        var c = await servico.SugerirAsync(TipoRefeicao.Almoco, null, default);

        c.Opcoes.Should().NotBeEmpty();
        c.Opcoes[0].Itens.Select(i => i.Alimento).Should().Contain("Frango, peito, sem pele, grelhado");
        c.Opcoes.SelectMany(o => o.Itens).Should().NotContain(i => i.Alimento.Contains("Macarrão") || i.Alimento.Contains("fígado"));
        c.Opcoes[0].Total.Kcal.Should().BeLessThan(c.Alvo.Kcal * 1.2m);
    }

    [Fact]
    public async Task Cardapio_da_noite_completa_a_proteina_que_falta_no_dia()
    {
        // como em 17/11: dia com muito carbo e pouca proteína até a tarde (~1730 kcal, ~63 g de proteína)
        var frango = await _db.Alimentos.SingleAsync(a => a.Nome == "Frango, peito, sem pele, grelhado");
        var arroz = await _db.Alimentos.SingleAsync(a => a.Nome == "Arroz, tipo 1, cozido");
        // café, almoço e tarde registrados: só a noite falta, com o resto do dia
        foreach (var (tipo, gFrango, gArroz) in new[] { (TipoRefeicao.CafeDaManha, 0, 280), (TipoRefeicao.Almoco, 100, 340), (TipoRefeicao.Tarde, 0, 610) })
            await Refeicoes().CriarAsync(new NovaRefeicaoDto
            {
                DataHora = _relogio.Agora, Tipo = tipo,
                Itens = [.. gFrango > 0 ? [new NovoItemRefeicaoDto { AlimentoId = frango.Id, Gramas = gFrango }] : Array.Empty<NovoItemRefeicaoDto>(),
                         new() { AlimentoId = arroz.Id, Gramas = gArroz }]
            }, default);

        var c = await new CardapioService(_db, _usuario, _relogio, Painel()).SugerirAsync(TipoRefeicao.Noite, null, default);

        c.Opcoes.Should().NotBeEmpty();
        c.Opcoes.Should().OnlyContain(o => o.Total.ProteinaG >= c.Alvo.ProteinaG * 0.9m,
            $"alvo P {c.Alvo.ProteinaG:0}: " + string.Join(" | ", c.Opcoes.Select(o =>
                $"{string.Join(" + ", o.Itens.Select(i => $"{i.Alimento} {i.Gramas:0} g"))} = P {o.Total.ProteinaG:0}, {o.Total.Kcal:0} kcal")));

        // a melhor opção encaixa os quatro macros, não só a proteína
        var melhor = c.Opcoes[0].Total;
        melhor.Kcal.Should().BeApproximately(c.Alvo.Kcal, c.Alvo.Kcal * 0.1m);
        melhor.ProteinaG.Should().BeApproximately(c.Alvo.ProteinaG, c.Alvo.ProteinaG * 0.1m);
        melhor.CarboG.Should().BeApproximately(c.Alvo.CarboG, c.Alvo.CarboG * 0.1m);
        melhor.GorduraG.Should().BeApproximately(c.Alvo.GorduraG, c.Alvo.GorduraG * 0.2m);
    }

    [Fact]
    public async Task Relatorio_semanal_conta_aderencia_e_proteina()
    {
        var frango = await _db.Alimentos.SingleAsync(a => a.Nome == "Frango, peito, sem pele, grelhado");
        // dia 1: perto da meta (2830 kcal ≈ 1780 g de frango); dia 2: bem abaixo
        await Refeicoes().CriarAsync(new NovaRefeicaoDto { DataHora = _relogio.Agora, Tipo = TipoRefeicao.Almoco,
            Itens = [new() { AlimentoId = frango.Id, Gramas = 1500 }, new() { AlimentoId = frango.Id, Gramas = 280 }] }, default);
        await Refeicoes().CriarAsync(new NovaRefeicaoDto { DataHora = _relogio.Agora.AddDays(-1), Tipo = TipoRefeicao.Almoco,
            Itens = [new() { AlimentoId = frango.Id, Gramas = 300 }] }, default);

        // meta vigente desde a semana anterior (o seed a cria "hoje")
        (await _db.Metas.SingleAsync()).VigenteDesde = new DateOnly(2026, 11, 1);
        await _db.SaveChangesAsync();
        var r = await new RelatorioService(_db, _usuario, _relogio, Metas()).GerarAsync("semana", null, default);

        r.Dias.Should().HaveCount(7);
        r.DiasRegistrados.Should().Be(2);
        r.DiasNaMetaKcal.Should().Be(1);
        r.DiasProteinaAtingida.Should().Be(1);
        r.Destaques.Should().Contain(d => d.Contains("Registro em 2 de 7"));
    }

    [Fact]
    public async Task Relatorio_mensal_tem_30_dias_e_periodo_invalido_falha()
    {
        var s = new RelatorioService(_db, _usuario, _relogio, Metas());
        (await s.GerarAsync("mes", null, default)).Dias.Should().HaveCount(30);
        var act = () => s.GerarAsync("ano", null, default);
        await act.Should().ThrowAsync<ValidacaoException>();
    }

    [Fact]
    public async Task Foto_do_corpo_e_bloqueada_com_provedor_gratis_sem_enviar_a_imagem()
    {
        var s = new AvaliacaoCorporalService(new VisaoComPessoas(), new SanitizadorImagem(), Perfil(), NullLogger<AvaliacaoCorporalService>.Instance);
        s.Situacao().Permitido.Should().BeFalse();
        // VisaoComPessoas lança se AnalisarCorpoAsync for chamado: o bloqueio acontece antes
        var r = await s.AnalisarAsync(Jpeg(), null, default);
        r.Permitido.Should().BeFalse();
        r.MotivoBloqueio.Should().Contain("Claude");
    }

    [Fact]
    public async Task Foto_do_corpo_com_provedor_permitido_devolve_faixa_e_nao_grava_nada()
    {
        var s = new AvaliacaoCorporalService(new FakeVisionProvider(), new SanitizadorImagem(), Perfil(), NullLogger<AvaliacaoCorporalService>.Instance);
        var r = await s.AnalisarAsync(Jpeg(), "manhã", default);
        r.Permitido.Should().BeTrue();
        r.GorduraMinPercent.Should().Be(14);
        r.GorduraMaxPercent.Should().Be(18);
        (await _db.Medidas.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(14.0, 18.0, 14.0, 18.0)]
    [InlineData(18.0, 14.0, 14.0, 18.0)]
    [InlineData(1.0, 5.0, null, null)]
    [InlineData(10.0, 40.0, null, null)]
    public void Faixa_de_gordura_e_validada(double min, double max, double? emin, double? emax)
    {
        var (a, b) = AvaliacaoCorporalService.ValidarFaixa((decimal)min, (decimal)max);
        a.Should().Be((decimal?)emin);
        b.Should().Be((decimal?)emax);
    }

    [Fact]
    public async Task Medidas_guardam_so_a_faixa_e_o_texto()
    {
        var p = new ProgressoService(_db, _usuario, _relogio, Perfil());
        var m = await p.RegistrarMedidasAsync(new NovasMedidasDto
        {
            CinturaCm = 82, PescocoCm = 37, ObservacaoCorporal = "abdômen com definição leve",
            GorduraFotoMinPercent = 14, GorduraFotoMaxPercent = 18
        }, default);
        m.GorduraFotoMinPercent.Should().Be(14);
        typeof(Medidas).GetProperties().Should().NotContain(pr => pr.PropertyType == typeof(byte[]), "nenhuma coluna de imagem");
    }
}
