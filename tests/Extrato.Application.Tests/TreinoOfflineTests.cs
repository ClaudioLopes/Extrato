using Extrato.Application;
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

/// <summary>Treino offline: idempotência da sessão e das séries, sessão iniciada sem internet e recálculo no aparelho.</summary>
public class TreinoOfflineTests : IAsyncLifetime
{
    private readonly ExtratoDbContext _db = new(new DbContextOptionsBuilder<ExtratoDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly RelogioFixo _relogio = new(new DateTime(2026, 11, 14, 18, 0, 0));
    private Aparelho _legPress = null!;
    private Aparelho _supino = null!;
    private Rotina _rotinaA = null!;

    public async Task InitializeAsync()
    {
        await SemeadorDados.SemearAsync(_db, _relogio);
        var academia = new Academia { Nome = "Academia do bairro" };
        _legPress = new Aparelho { Academia = academia, Nome = "Leg press", Exercicio = "Leg press 45°", GrupoMuscular = "Pernas", TipoCarga = TipoCarga.Pilha };
        _supino = new Aparelho { Academia = academia, Nome = "Supino", Exercicio = "Supino reto", GrupoMuscular = "Peito", TipoCarga = TipoCarga.Anilha };
        _db.Aparelhos.AddRange(_legPress, _supino);
        await _db.SaveChangesAsync();
        _rotinaA = new Rotina { Nome = "A", Itens = [new RotinaItem { AparelhoId = _legPress.Id, Ordem = 1, SeriesAlvo = 3, RepsMin = 8, RepsMax = 12 }] };
        _db.Rotinas.Add(_rotinaA);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private TreinoService Treino() => new(_db, new UsuarioPadrao(), _relogio, new FakeVisionProvider(), new SanitizadorImagem(),
        NullLogger<TreinoService>.Instance);

    private static NovaSerieDto Serie(int aparelhoId, decimal carga, int reps, Guid? idCliente = null) =>
        new() { AparelhoId = aparelhoId, CargaKg = carga, Reps = reps, IdCliente = idCliente };

    /// <summary>Sessão de ontem no leg press: vira a "última sessão" que pré-preenche a de hoje.</summary>
    private async Task SessaoAnterior()
    {
        var ontem = await Treino().IniciarSessaoAsync(new NovaSessaoDto { RotinaId = _rotinaA.Id, DataHora = _relogio.Agora.AddDays(-1) }, default);
        await Treino().RegistrarSerieAsync(ontem.Id, Serie(_legPress.Id, 100, 10), default);
        await Treino().RegistrarSerieAsync(ontem.Id, Serie(_legPress.Id, 110, 8), default);
    }

    [Fact]
    public async Task Sessao_reenviada_com_o_mesmo_IdCliente_nao_duplica_e_guarda_a_hora_do_aparelho()
    {
        var id = Guid.NewGuid();
        var inicio = _relogio.Agora.AddHours(-2); // iniciada offline, enviada 2 h depois
        var primeira = await Treino().IniciarSessaoAsync(new NovaSessaoDto { IdCliente = id, RotinaId = _rotinaA.Id, DataHora = inicio }, default);
        var reenvio = await Treino().IniciarSessaoAsync(new NovaSessaoDto { IdCliente = id, RotinaId = _rotinaA.Id, DataHora = inicio }, default);

        reenvio.Id.Should().Be(primeira.Id);
        primeira.DataHora.Should().Be(inicio);
        (await _db.Sessoes.CountAsync()).Should().Be(1);
        (await Treino().SessaoPorClienteAsync(id, default)).Id.Should().Be(primeira.Id);
    }

    [Fact]
    public async Task Sem_DataHora_a_sessao_usa_a_hora_do_servidor()
    {
        var s = await Treino().IniciarSessaoAsync(new NovaSessaoDto(), default);
        s.DataHora.Should().Be(_relogio.Agora);
    }

    [Fact]
    public async Task Serie_reenviada_com_o_mesmo_IdCliente_nao_duplica()
    {
        var sessao = await Treino().IniciarSessaoAsync(new NovaSessaoDto(), default);
        var id = Guid.NewGuid();
        var primeira = await Treino().RegistrarSerieAsync(sessao.Id, Serie(_supino.Id, 60, 10, id), default);
        var reenvio = await Treino().RegistrarSerieAsync(sessao.Id, Serie(_supino.Id, 60, 10, id), default);

        reenvio.Id.Should().Be(primeira.Id);
        reenvio.IdCliente.Should().Be(id);
        (await _db.Series.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Serie_de_sessao_iniciada_offline_e_gravada_pelo_IdCliente_da_sessao()
    {
        var idSessao = Guid.NewGuid();
        var sessao = await Treino().IniciarSessaoAsync(new NovaSessaoDto { IdCliente = idSessao }, default);
        await Treino().RegistrarSeriePorClienteAsync(idSessao, Serie(_supino.Id, 60, 10), default);

        (await Treino().SessaoAsync(sessao.Id, default)).Exercicios.Single().SeriesHoje.Should().ContainSingle();
    }

    [Fact]
    public async Task Serie_de_sessao_que_nao_chegou_ao_servidor_e_recusada()
    {
        var acao = () => Treino().RegistrarSeriePorClienteAsync(Guid.NewGuid(), Serie(_supino.Id, 60, 10), default);
        await acao.Should().ThrowAsync<NaoEncontradoException>();
    }

    [Fact]
    public async Task Ultimas_series_trazem_so_a_sessao_mais_recente_de_cada_aparelho()
    {
        var antiga = await Treino().IniciarSessaoAsync(new NovaSessaoDto { DataHora = _relogio.Agora.AddDays(-7) }, default);
        await Treino().RegistrarSerieAsync(antiga.Id, Serie(_legPress.Id, 80, 12), default);
        await SessaoAnterior();

        var ultimas = await Treino().UltimasSeriesAsync(default);

        ultimas.Should().ContainKey(_legPress.Id).WhoseValue.Select(s => s.CargaKg).Should().Equal(100, 110);
        ultimas.Should().NotContainKey(_supino.Id, "sem série registrada");
    }

    [Fact]
    public async Task Sessao_montada_offline_bate_com_a_do_servidor_depois_do_envio()
    {
        await SessaoAnterior();

        // dados salvos no aparelho antes de ficar sem internet
        var aparelhos = await Treino().AparelhosAsync(null, default);
        var rotina = (await Treino().RotinasAsync(default)).Single();
        var ultimas = await Treino().UltimasSeriesAsync(default);

        // offline: inicia a sessão da rotina A, faz 2 séries de leg press e 1 de supino (avulso)
        var idSessao = Guid.NewGuid();
        var inicio = _relogio.Agora;
        var pendentes = new List<SeriePendente>
        {
            new(Guid.NewGuid(), _legPress.Id, 110, 9, null, inicio.AddMinutes(5)),
            new(Guid.NewGuid(), _supino.Id, 60, 10, 2, inicio.AddMinutes(15)),
            new(Guid.NewGuid(), _legPress.Id, 115, 8, null, inicio.AddMinutes(8)),
        };
        var offline = TreinoLocal.Combinar(TreinoLocal.NovaSessao(inicio, rotina, aparelhos, ultimas), pendentes, aparelhos, ultimas);

        // a fila é enviada na ordem em que as coisas aconteceram
        await Treino().IniciarSessaoAsync(new NovaSessaoDto { IdCliente = idSessao, RotinaId = rotina.Id, DataHora = inicio }, default);
        foreach (var p in pendentes.OrderBy(p => p.CriadaEm))
            await Treino().RegistrarSeriePorClienteAsync(idSessao, new NovaSerieDto
            {
                AparelhoId = p.AparelhoId, CargaKg = p.CargaKg, Reps = p.Reps, RepsNaReserva = p.RepsNaReserva, IdCliente = p.IdCliente
            }, default);
        var servidor = await Treino().SessaoPorClienteAsync(idSessao, default);

        offline.Id.Should().Be(0);
        offline.Rotina.Should().Be("A");
        offline.Exercicios.Select(e => e.Aparelho.Id).Should().Equal(servidor.Exercicios.Select(e => e.Aparelho.Id));
        foreach (var (o, s) in offline.Exercicios.Zip(servidor.Exercicios))
        {
            o.SeriesHoje.Select(x => (x.Ordem, x.CargaKg, x.Reps, x.RepsNaReserva, x.IdCliente))
                .Should().Equal(s.SeriesHoje.Select(x => (x.Ordem, x.CargaKg, x.Reps, x.RepsNaReserva, x.IdCliente)));
            o.SeriesHoje.Should().OnlyContain(x => x.Pendente);
            o.UltimaSessao.Select(x => (x.CargaKg, x.Reps)).Should().Equal(s.UltimaSessao.Select(x => (x.CargaKg, x.Reps)));
            o.VolumeHoje.Should().Be(s.VolumeHoje);
            o.Melhor1RmHoje.Should().Be(s.Melhor1RmHoje);
            o.VolumeAnterior.Should().Be(s.VolumeAnterior);
            o.Melhor1RmAnterior.Should().Be(s.Melhor1RmAnterior);
            (o.SeriesAlvo, o.RepsMin, o.RepsMax).Should().Be((s.SeriesAlvo, s.RepsMin, s.RepsMax));
        }
    }

    [Fact]
    public void Sem_pendentes_a_sessao_volta_igual()
    {
        var sessao = new SessaoDto(5, _relogio.Agora, null, null, null, []);
        TreinoLocal.Combinar(sessao, [], [], new Dictionary<int, List<SerieDto>>()).Should().BeSameAs(sessao);
    }
}
