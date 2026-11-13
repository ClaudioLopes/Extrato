using Extrato.Application.Servicos;
using Extrato.Contracts;
using Extrato.Infrastructure;
using Extrato.Infrastructure.Dados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Extrato.Application.Tests;

public class AutenticacaoTests : IAsyncLifetime
{
    private const string SenhaBoa = "morango-whey-2026";
    private readonly ExtratoDbContext _db = new(new DbContextOptionsBuilder<ExtratoDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public Task InitializeAsync() => SemeadorDados.SemearAsync(_db, new RelogioFixo(new DateTime(2026, 11, 13, 9, 0, 0)));
    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private AutenticacaoService Servico() =>
        new(_db, new UsuarioPadrao(), new HasherSenhaPbkdf2(), NullLogger<AutenticacaoService>.Instance);

    private Task<string> Configurar(bool local = true, string? codigoEnviado = null, string? codigoServidor = null, string senha = SenhaBoa) =>
        Servico().ConfigurarAsync(new ConfigurarAcessoDto { Login = " Eu.Mesmo ", Senha = senha, Codigo = codigoEnviado }, local, codigoServidor, default);

    [Fact]
    public async Task Primeiro_acesso_local_cria_credencial_com_hash()
    {
        (await Servico().ConfiguradoAsync(default)).Should().BeFalse();
        var login = await Configurar();

        login.Should().Be("eu.mesmo");
        var cred = await _db.Credenciais.SingleAsync();
        cred.SenhaHash.Should().NotContain(SenhaBoa).And.NotBeNullOrWhiteSpace();
        (await Servico().ConfiguradoAsync(default)).Should().BeTrue();
    }

    [Fact]
    public async Task Primeiro_acesso_so_pode_acontecer_uma_vez()
    {
        await Configurar();
        var act = () => Configurar();
        await act.Should().ThrowAsync<ValidacaoException>().WithMessage("*já foi configurado*");
    }

    [Fact]
    public async Task Primeiro_acesso_remoto_sem_codigo_configurado_e_bloqueado()
    {
        var act = () => Configurar(local: false);
        await act.Should().ThrowAsync<ValidacaoException>();
        (await _db.Credenciais.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Primeiro_acesso_remoto_exige_o_codigo_certo()
    {
        var errado = () => Configurar(local: false, codigoEnviado: "chute", codigoServidor: "codigo-secreto");
        await errado.Should().ThrowAsync<ValidacaoException>().WithMessage("*Código*");

        await Configurar(local: false, codigoEnviado: "codigo-secreto", codigoServidor: "codigo-secreto");
        (await _db.Credenciais.AnyAsync()).Should().BeTrue();
    }

    [Theory]
    [InlineData("curta")]
    [InlineData("aaaaaaaaaaaa")]
    [InlineData("eu.mesmo-2026!")]
    public async Task Senha_fraca_e_rejeitada(string senha)
    {
        var act = () => Configurar(senha: senha);
        await act.Should().ThrowAsync<ValidacaoException>();
    }

    [Fact]
    public async Task Login_valida_senha_e_ignora_maiusculas_no_login()
    {
        await Configurar();
        (await Servico().ValidarAsync("EU.MESMO", SenhaBoa, default)).Should().Be("eu.mesmo");
        (await Servico().ValidarAsync("eu.mesmo", "senha-errada-123", default)).Should().BeNull();
        (await Servico().ValidarAsync("outro", SenhaBoa, default)).Should().BeNull();
    }

    [Fact]
    public async Task Sem_acesso_configurado_ninguem_entra()
    {
        (await Servico().ValidarAsync("eu.mesmo", SenhaBoa, default)).Should().BeNull();
    }

    [Fact]
    public async Task Troca_de_senha_exige_a_senha_atual()
    {
        await Configurar();
        var errada = () => Servico().TrocarSenhaAsync(new TrocarSenhaDto { SenhaAtual = "nao-e-essa-1", NovaSenha = "nova-senha-forte-9" }, default);
        await errada.Should().ThrowAsync<ValidacaoException>();

        await Servico().TrocarSenhaAsync(new TrocarSenhaDto { SenhaAtual = SenhaBoa, NovaSenha = "nova-senha-forte-9" }, default);
        (await Servico().ValidarAsync("eu.mesmo", SenhaBoa, default)).Should().BeNull();
        (await Servico().ValidarAsync("eu.mesmo", "nova-senha-forte-9", default)).Should().Be("eu.mesmo");
    }

    [Fact]
    public void Hash_corrompido_nao_autentica_nem_quebra() =>
        new HasherSenhaPbkdf2().Verificar("isso-nao-e-um-hash", SenhaBoa, out _).Should().BeFalse();
}
