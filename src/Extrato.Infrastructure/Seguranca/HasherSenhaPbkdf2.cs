using Extrato.Application.Servicos;
using Extrato.Domain.Entidades;
using Microsoft.AspNetCore.Identity;

namespace Extrato.Infrastructure;

/// <summary>PBKDF2 (HMAC-SHA512, salt aleatório, iterações versionadas) via PasswordHasher do ASP.NET Core.</summary>
public class HasherSenhaPbkdf2 : IHasherSenha
{
    private readonly PasswordHasher<Credencial> _hasher = new();
    private static readonly Credencial Usuario = new();

    public string Gerar(string senha) => _hasher.HashPassword(Usuario, senha);

    public bool Verificar(string hash, string senha, out bool precisaRehash)
    {
        PasswordVerificationResult r;
        try { r = _hasher.VerifyHashedPassword(Usuario, hash, senha); }
        catch (FormatException) { precisaRehash = false; return false; }
        precisaRehash = r == PasswordVerificationResult.SuccessRehashNeeded;
        return r != PasswordVerificationResult.Failed;
    }
}
