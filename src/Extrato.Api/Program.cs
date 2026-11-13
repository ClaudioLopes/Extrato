using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Extrato.Api;
using Extrato.Application;
using Extrato.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorErros>();
builder.Services.AddOpenApi();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 16 * 1024 * 1024);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

// Chaves que cifram o cookie: persistidas para a sessão sobreviver a reinícios (no Docker, num volume).
if (builder.Configuration["Auth:PastaChaves"] is { Length: > 0 } pastaChaves)
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(pastaChaves)).SetApplicationName("extrato");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "extrato.sessao";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict; // bloqueia envio do cookie por outros sites (CSRF)
        // fora de dev exige HTTPS; Auth:CookieSomenteHttps=false libera http (ex.: demo local no Docker)
        o.Cookie.SecurePolicy = builder.Configuration.GetValue("Auth:CookieSomenteHttps", !builder.Environment.IsDevelopment())
            ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        // API: responde 401/403 em vez de redirecionar para uma página de login
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });

// Tudo exige login, exceto o que for marcado com AllowAnonymous.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(EndpointsAcesso.PoliticaLogin, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

// arquivos do Blazor são públicos (o app sem dados); os dados ficam na API protegida
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapAcesso();
app.MapNutricao();
app.MapTreino();
app.MapFallbackToFile("index.html").AllowAnonymous();

if (app.Configuration.GetValue("Extrato:InicializarBanco", true))
    await app.Services.InicializarBancoAsync();

app.Run();

public partial class Program;
