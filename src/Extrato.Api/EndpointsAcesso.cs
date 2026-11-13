using System.Net;
using System.Security.Claims;
using Extrato.Application;
using Extrato.Application.Servicos;
using Extrato.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Extrato.Api;

public static class EndpointsAcesso
{
    public const string PoliticaLogin = "login";

    public static void MapAcesso(this IEndpointRouteBuilder app)
    {
        var acesso = app.MapGroup("/api/auth").AddEndpointFilter<ValidacaoFiltro>();

        acesso.MapGet("/sessao", async (HttpContext ctx, AutenticacaoService s, CancellationToken ct) =>
        {
            var autenticado = ctx.User.Identity?.IsAuthenticated == true;
            return new SessaoUsuarioDto(await s.ConfiguradoAsync(ct), autenticado, autenticado ? ctx.User.Identity!.Name : null);
        }).AllowAnonymous();

        acesso.MapPost("/configurar", async (ConfigurarAcessoDto dto, HttpContext ctx, AutenticacaoService s,
            IOptions<OpcoesAcesso> opcoes, CancellationToken ct) =>
        {
            var login = await s.ConfigurarAsync(dto, RequisicaoLocal(ctx), opcoes.Value.CodigoConfiguracao, ct);
            await Entrar(ctx, login, lembrar: true);
            return new SessaoUsuarioDto(true, true, login);
        }).AllowAnonymous().RequireRateLimiting(PoliticaLogin);

        acesso.MapPost("/login", async (LoginDto dto, HttpContext ctx, AutenticacaoService s, CancellationToken ct) =>
        {
            var login = await s.ValidarAsync(dto.Login, dto.Senha, ct);
            if (login is null)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Acesso negado", detail: "Login ou senha incorretos.");
            await Entrar(ctx, login, dto.Lembrar);
            return Results.Ok(new SessaoUsuarioDto(true, true, login));
        }).AllowAnonymous().RequireRateLimiting(PoliticaLogin);

        acesso.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AllowAnonymous();

        acesso.MapPost("/senha", async (TrocarSenhaDto dto, AutenticacaoService s, CancellationToken ct) =>
        {
            await s.TrocarSenhaAsync(dto, ct);
            return Results.NoContent();
        }).RequireRateLimiting(PoliticaLogin);
    }

    private static Task Entrar(HttpContext ctx, string login, bool lembrar)
    {
        var identidade = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, login), new Claim(ClaimTypes.NameIdentifier, "1")],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identidade),
            new AuthenticationProperties { IsPersistent = lembrar });
    }

    /// <summary>Primeiro acesso sem código só do próprio servidor (sem proxy no meio).</summary>
    private static bool RequisicaoLocal(HttpContext ctx)
    {
        var remoto = ctx.Connection.RemoteIpAddress;
        if (remoto is null || !IPAddress.IsLoopback(remoto)) return false;
        // atrás de proxy reverso o IP remoto é o do proxy: não confiar como "local"
        return !ctx.Request.Headers.ContainsKey("X-Forwarded-For");
    }
}
