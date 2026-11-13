using System.Security.Cryptography;
using System.Text;
using Extrato.Contracts;
using Extrato.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Extrato.Application.Servicos;

public interface IHasherSenha
{
    string Gerar(string senha);
    /// <summary>Verifica a senha; <paramref name="precisaRehash"/> indica hash com parâmetros antigos.</summary>
    bool Verificar(string hash, string senha, out bool precisaRehash);
}

public class OpcoesAcesso
{
    public const string Secao = "Auth";
    /// <summary>
    /// Código exigido para criar a senha no primeiro acesso quando a requisição não vem do
    /// próprio servidor (localhost). Vazio: primeiro acesso só pelo localhost.
    /// </summary>
    public string? CodigoConfiguracao { get; set; }
}

/// <summary>Acesso do único usuário: primeiro acesso, login e troca de senha.</summary>
public class AutenticacaoService(IAppDbContext db, IUsuarioAtual usuario, IHasherSenha hasher, ILogger<AutenticacaoService> logger)
{
    public const int TamanhoMinimoSenha = 10;

    // hash fixo para gastar o mesmo tempo quando o login não existe (evita descobrir logins pelo tempo)
    private static string? _hashFicticio;

    public Task<bool> ConfiguradoAsync(CancellationToken ct) => db.Credenciais.AnyAsync(ct);

    /// <summary>Cria a senha. Só funciona uma vez, do localhost ou com o código de configuração.</summary>
    public async Task<string> ConfigurarAsync(ConfigurarAcessoDto dto, bool requisicaoLocal, string? codigoEsperado, CancellationToken ct)
    {
        if (await ConfiguradoAsync(ct))
            throw new ValidacaoException("O acesso já foi configurado. Entre com seu login e senha.");

        if (!requisicaoLocal)
        {
            if (string.IsNullOrWhiteSpace(codigoEsperado))
                throw new ValidacaoException("O primeiro acesso só pode ser feito no próprio computador do servidor, ou defina Auth:CodigoConfiguracao.");
            if (!IguaisTempoConstante(dto.Codigo ?? "", codigoEsperado))
                throw new ValidacaoException("Código de configuração inválido.");
        }

        var login = NormalizarLogin(dto.Login);
        ValidarSenha(dto.Senha, login);
        if (!await db.Usuarios.AnyAsync(u => u.Id == usuario.UsuarioId, ct))
            throw new ValidacaoException("Usuário não encontrado.");

        db.Credenciais.Add(new Credencial { UsuarioId = usuario.UsuarioId, Login = login, SenhaHash = hasher.Gerar(dto.Senha) });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Acesso configurado.");
        return login;
    }

    /// <summary>Devolve o login normalizado se a senha confere; null caso contrário.</summary>
    public async Task<string?> ValidarAsync(string login, string senha, CancellationToken ct)
    {
        var normalizado = NormalizarLogin(login);
        var cred = await db.Credenciais.FirstOrDefaultAsync(c => c.Login == normalizado, ct);
        if (cred is null)
        {
            _hashFicticio ??= hasher.Gerar(Guid.NewGuid().ToString());
            hasher.Verificar(_hashFicticio, senha, out _);
            return null;
        }

        if (!hasher.Verificar(cred.SenhaHash, senha, out var rehash)) return null;
        if (rehash)
        {
            cred.SenhaHash = hasher.Gerar(senha);
            cred.AtualizadaEm = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return cred.Login;
    }

    public async Task TrocarSenhaAsync(TrocarSenhaDto dto, CancellationToken ct)
    {
        var cred = await db.Credenciais.FirstOrDefaultAsync(c => c.UsuarioId == usuario.UsuarioId, ct)
            ?? throw new NaoEncontradoException("Acesso não configurado.");
        if (!hasher.Verificar(cred.SenhaHash, dto.SenhaAtual, out _))
            throw new ValidacaoException("Senha atual incorreta.");
        ValidarSenha(dto.NovaSenha, cred.Login);
        cred.SenhaHash = hasher.Gerar(dto.NovaSenha);
        cred.AtualizadaEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    internal static string NormalizarLogin(string login)
    {
        var l = (login ?? "").Trim().ToLowerInvariant();
        if (l.Length < 3) throw new ValidacaoException("Login muito curto.");
        return l;
    }

    internal static void ValidarSenha(string senha, string login)
    {
        if (string.IsNullOrEmpty(senha) || senha.Length < TamanhoMinimoSenha)
            throw new ValidacaoException($"A senha precisa de pelo menos {TamanhoMinimoSenha} caracteres.");
        if (senha.Contains(login, StringComparison.OrdinalIgnoreCase))
            throw new ValidacaoException("A senha não pode conter o login.");
        if (senha.Distinct().Count() < 4)
            throw new ValidacaoException("Senha fraca demais: use caracteres variados.");
    }

    /// <summary>
    /// Carimbo da credencial guardado na sessão: muda quando a senha muda (o hash tem salt novo).
    /// Sessões com carimbo antigo, ou de uma credencial que não existe mais, são recusadas.
    /// </summary>
    public static string Carimbo(string senhaHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(senhaHash)))[..20];

    /// <summary>Carimbo atual do login, ou null se a credencial não existe.</summary>
    public async Task<string?> CarimboAtualAsync(string login, CancellationToken ct)
    {
        var normalizado = (login ?? "").Trim().ToLowerInvariant();
        var hash = await db.Credenciais.Where(c => c.Login == normalizado).Select(c => c.SenhaHash).FirstOrDefaultAsync(ct);
        return hash is null ? null : Carimbo(hash);
    }

    private static bool IguaisTempoConstante(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
