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

/// <summary>Modo offline: idempotência no servidor, catálogo e painel recalculado no aparelho.</summary>
public class OfflineTests : IAsyncLifetime
{
    private readonly ExtratoDbContext _db = new(new DbContextOptionsBuilder<ExtratoDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly RelogioFixo _relogio = new(new DateTime(2026, 11, 13, 13, 0, 0));
    private readonly UsuarioPadrao _usuario = new();

    public Task InitializeAsync() => SemeadorDados.SemearAsync(_db, _relogio);
    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private RefeicaoService Refeicoes() => new(_db, _usuario, _relogio, new FakeVisionProvider(), new SanitizadorImagem(),
        new AlimentoService(_db), NullLogger<RefeicaoService>.Instance);

    private PainelService Painel()
    {
        var perfil = new PerfilService(_db, _usuario, _relogio);
        var treino = new TreinoService(_db, _usuario, _relogio, new FakeVisionProvider(), new SanitizadorImagem(), NullLogger<TreinoService>.Instance);
        return new PainelService(_relogio, new MetaService(_db, _usuario, _relogio, perfil, treino), perfil, Refeicoes(),
            new FavoritoService(_db, _usuario, Refeicoes()));
    }

    private async Task<NovaRefeicaoDto> Almoco(Guid? idCliente = null)
    {
        var frango = await _db.Alimentos.SingleAsync(a => a.Nome == "Frango, peito, sem pele, grelhado");
        return new NovaRefeicaoDto
        {
            IdCliente = idCliente, Tipo = TipoRefeicao.Almoco, DataHora = _relogio.Agora,
            Itens = [new NovoItemRefeicaoDto { AlimentoId = frango.Id, Gramas = 200 }]
        };
    }

    [Fact]
    public async Task Reenvio_com_o_mesmo_IdCliente_nao_duplica()
    {
        var id = Guid.NewGuid();
        var primeira = await Refeicoes().CriarAsync(await Almoco(id), default);
        var reenvio = await Refeicoes().CriarAsync(await Almoco(id), default);

        reenvio.Id.Should().Be(primeira.Id);
        (await _db.Refeicoes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Sem_IdCliente_cada_envio_e_uma_refeicao()
    {
        await Refeicoes().CriarAsync(await Almoco(), default);
        await Refeicoes().CriarAsync(await Almoco(), default);
        (await _db.Refeicoes.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Catalogo_traz_todos_os_alimentos_com_sinonimos_e_versao_estavel()
    {
        var s = new AlimentoService(_db);
        var c1 = await s.CatalogoAsync(default);
        c1.Itens.Should().HaveCount(await _db.Alimentos.CountAsync());
        c1.Itens.Single(i => i.Alimento.Nome == "Arroz, tipo 1, cozido").Termos.Should().Contain("arroz");
        (await s.CatalogoAsync(default)).Versao.Should().Be(c1.Versao);

        await s.CriarAsync(new NovoAlimentoDto { Nome = "Barra de proteína X", Kcal100g = 380, Proteina100g = 30, Carbo100g = 40, Gordura100g = 10 }, default);
        (await s.CatalogoAsync(default)).Versao.Should().NotBe(c1.Versao, "o app baixa de novo quando algo muda");
    }

    [Fact]
    public async Task Busca_offline_da_o_mesmo_primeiro_resultado_que_o_servidor()
    {
        var s = new AlimentoService(_db);
        var catalogo = (await s.CatalogoAsync(default)).Itens;
        foreach (var termo in new[] { "arroz", "feijao", "frango grelhado", "whey", "pão francês", "feijoada" })
        {
            var servidor = (await s.BuscarAsync(termo, 5, default)).First();
            var local = PainelLocal.Buscar(catalogo, termo, 5).First();
            local.Id.Should().Be(servidor.Id, $"busca por '{termo}'");
        }
    }

    [Fact]
    public async Task Painel_offline_com_pendente_bate_com_o_servidor_depois_do_envio()
    {
        // painel salvo antes de ficar offline
        var salvo = await Painel().DiaAsync(null, default);
        var regras = (await new PerfilService(_db, _usuario, _relogio).ObterRegrasDtoAsync(default));

        // lançamento feito offline: prévia montada no aparelho a partir do catálogo
        var dto = await Almoco(Guid.NewGuid());
        var catalogo = (await new AlimentoService(_db).CatalogoAsync(default)).Itens.ToDictionary(i => i.Alimento.Id, i => i.Alimento);
        var previa = PainelLocal.RefeicaoLocal(dto, catalogo, _relogio.Agora);
        var offline = PainelLocal.Combinar(salvo, [previa], regras, _relogio.Agora, DateTime.Now);

        // depois da sincronização, o servidor calcula o mesmo
        await Refeicoes().CriarAsync(dto, default);
        var servidor = await Painel().DiaAsync(null, default);

        offline.Consumido.Should().Be(servidor.Consumido);
        offline.Restante.Should().Be(servidor.Restante);
        offline.Refeicoes.Select(r => r.Alvo).Should().Equal(servidor.Refeicoes.Select(r => r.Alvo));
        offline.Alertas.Should().Contain(a => a.StartsWith("Sem conexão"));
        offline.Lancamentos.Should().ContainSingle(l => l.Id == 0);
    }

    [Fact]
    public void Pendente_de_outro_dia_nao_entra_no_painel()
    {
        var salvo = new PainelDiaDto(new DateOnly(2026, 11, 13), new MacrosDto(2000, 100, 250, 60), MacrosDto.Zero,
            new MacrosDto(2000, 100, 250, 60), [], [], []);
        var ontem = new RefeicaoDto(0, new DateTime(2026, 11, 12, 20, 0, 0), TipoRefeicao.Noite, OrigemRefeicao.Manual, null, [],
            new MacrosDto(500, 30, 50, 10));
        var r = PainelLocal.Combinar(salvo, [ontem], null, new DateTime(2026, 11, 13, 10, 0, 0), null);
        r.Consumido.Kcal.Should().Be(0);
    }
}
