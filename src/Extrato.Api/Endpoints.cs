using System.ComponentModel.DataAnnotations;
using Extrato.Application;
using Extrato.Application.Servicos;
using Extrato.Contracts;
using Extrato.Domain;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Extrato.Api;

public static class Endpoints
{
    public static void MapNutricao(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<ValidacaoFiltro>();

        api.MapGet("/perfil", (PerfilService s, CancellationToken ct) => s.ObterAsync(ct));
        api.MapPut("/perfil", (PerfilDto dto, PerfilService s, CancellationToken ct) => s.AtualizarAsync(dto, ct));
        api.MapGet("/regras", (PerfilService s, CancellationToken ct) => s.ObterRegrasDtoAsync(ct));
        api.MapPut("/regras", (RegrasDto dto, PerfilService s, CancellationToken ct) => s.AtualizarRegrasAsync(dto, ct));

        api.MapGet("/metas/atual", (MetaService s, CancellationToken ct) => s.AtualAsync(ct));
        api.MapPut("/metas/atual", (MetaManualDto dto, MetaService s, CancellationToken ct) => s.DefinirManualAsync(dto, ct));
        api.MapGet("/metas/simular", (MetaService s, CancellationToken ct) => s.SimularAsync(ct));
        api.MapPost("/metas/recalcular", (MetaService s, CancellationToken ct) => s.RecalcularAsync(ct));
        api.MapPost("/metas/ajuste-semanal", (MetaService s, CancellationToken ct) => s.AjusteSemanalAsync(ct));

        // multipart lido como Stream (sem IFormFile/CopyTo para disco): a imagem nunca toca o disco
        api.MapPost("/refeicoes/analisar-foto", async (HttpRequest req, RefeicaoService s, CancellationToken ct) =>
        {
            TipoRefeicao? tipo = null;
            string? descricao = null;
            RascunhoRefeicaoDto? rascunho = null;
            await LerMultipartAsync(req, ct,
                campo: (nome, valor) =>
                {
                    if (nome == "tipo" && Enum.TryParse<TipoRefeicao>(valor, true, out var t)) tipo = t;
                    if (nome == "descricao") descricao = string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
                },
                arquivo: async stream =>
                {
                    if (tipo is null) throw new ValidacaoException("Envie 'tipo' (e 'descricao') antes da imagem no multipart.");
                    rascunho = await s.AnalisarFotoAsync(stream, tipo.Value, descricao, ct);
                });
            return rascunho ?? throw new ValidacaoException("Imagem não enviada.");
        }).DisableAntiforgery();

        api.MapGet("/refeicoes", (DateOnly? data, RefeicaoService s, IRelogio r, CancellationToken ct) => s.ListarDoDiaAsync(data ?? r.Hoje, ct));
        api.MapPost("/refeicoes", (NovaRefeicaoDto dto, RefeicaoService s, CancellationToken ct) => s.CriarAsync(dto, ct));
        api.MapPut("/refeicoes/{id:int}", (int id, NovaRefeicaoDto dto, RefeicaoService s, CancellationToken ct) => s.EditarAsync(id, dto, ct));
        api.MapDelete("/refeicoes/{id:int}", async (int id, RefeicaoService s, CancellationToken ct) => { await s.RemoverAsync(id, ct); return Results.NoContent(); });

        api.MapGet("/alimentos", (string? busca, int? limite, AlimentoService s, CancellationToken ct) => s.BuscarAsync(busca, limite ?? 20, ct));
        api.MapPost("/alimentos", (NovoAlimentoDto dto, AlimentoService s, CancellationToken ct) => s.CriarAsync(dto, ct));
        api.MapPost("/alimentos/importar", async (HttpRequest req, string? fonte, AlimentoService s, CancellationToken ct) =>
        {
            var f = Enum.TryParse<FonteAlimento>(fonte, true, out var x) ? x : FonteAlimento.Taco;
            ImportacaoDto? r = null;
            await LerMultipartAsync(req, ct, (_, _) => { }, async stream => r = await s.ImportarCsvAsync(stream, f, ct));
            return r ?? throw new ValidacaoException("Arquivo CSV não enviado.");
        }).DisableAntiforgery();

        api.MapGet("/favoritos", (FavoritoService s, CancellationToken ct) => s.ListarAsync(ct));
        api.MapPost("/favoritos", (NovoFavoritoDto dto, FavoritoService s, CancellationToken ct) => s.CriarAsync(dto, ct));
        api.MapDelete("/favoritos/{id:int}", async (int id, FavoritoService s, CancellationToken ct) => { await s.RemoverAsync(id, ct); return Results.NoContent(); });
        api.MapPost("/favoritos/{id:int}/registrar", (int id, TipoRefeicao tipo, FavoritoService s, CancellationToken ct) => s.RegistrarAsync(id, tipo, ct));

        api.MapGet("/painel/dia", (DateOnly? data, PainelService s, CancellationToken ct) => s.DiaAsync(data, ct));
        api.MapGet("/cardapio", (TipoRefeicao tipo, DateOnly? data, CardapioService s, CancellationToken ct) => s.SugerirAsync(tipo, data, ct));
        api.MapGet("/relatorios", (string? periodo, DateOnly? ate, RelatorioService s, CancellationToken ct) => s.GerarAsync(periodo ?? "semana", ate, ct));

        // foto do corpo: mesmo cuidado da foto do prato (stream em memória, nada gravado) e só com provedor pago/local
        api.MapPost("/medidas/analisar-foto", async (HttpRequest req, AvaliacaoCorporalService s, CancellationToken ct) =>
        {
            string? descricao = null;
            var consentiu = false;
            AvaliacaoCorporalDto? r = null;
            await LerMultipartAsync(req, ct,
                (campo, valor) =>
                {
                    if (campo == "descricao") descricao = valor;
                    if (campo == "consentimento") consentiu = valor is "true" or "on" or "1";
                },
                async stream =>
                {
                    if (!consentiu) throw new ValidacaoException("Confirme o consentimento antes de enviar a foto (campo 'consentimento' antes da imagem).");
                    r = await s.AnalisarAsync(stream, descricao, ct);
                });
            return r ?? throw new ValidacaoException("Imagem não enviada.");
        }).DisableAntiforgery();
        api.MapGet("/medidas/foto-permitida", (AvaliacaoCorporalService s) => s.Situacao());

        api.MapPost("/peso", (NovoPesoDto dto, ProgressoService s, CancellationToken ct) => s.RegistrarPesoAsync(dto, ct));
        api.MapPost("/medidas", (NovasMedidasDto dto, ProgressoService s, CancellationToken ct) => s.RegistrarMedidasAsync(dto, ct));
        api.MapGet("/progresso", (DateOnly? de, DateOnly? ate, ProgressoService s, CancellationToken ct) => s.ObterAsync(de, ate, ct));

        api.MapGet("/visao/provedor", (IVisionProvider v) => new { v.Nome });
    }

    public static void MapTreino(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<ValidacaoFiltro>();

        api.MapGet("/academias", (TreinoService s, CancellationToken ct) => s.AcademiasAsync(ct));
        api.MapPost("/academias", (NovaAcademiaDto dto, TreinoService s, CancellationToken ct) => s.CriarAcademiaAsync(dto, ct));

        api.MapPost("/aparelhos/analisar-foto", async (HttpRequest req, TreinoService s, CancellationToken ct) =>
        {
            string? nome = null;
            RascunhoAparelhoDto? r = null;
            await LerMultipartAsync(req, ct,
                (campo, valor) => { if (campo == "nome") nome = valor; },
                async stream => r = await s.AnalisarFotoAparelhoAsync(stream, nome, ct));
            return r ?? throw new ValidacaoException("Imagem não enviada.");
        }).DisableAntiforgery();
        api.MapGet("/aparelhos", (int? academiaId, TreinoService s, CancellationToken ct) => s.AparelhosAsync(academiaId, ct));
        api.MapPost("/aparelhos", (NovoAparelhoDto dto, TreinoService s, CancellationToken ct) => s.CriarAparelhoAsync(dto, ct));

        api.MapGet("/rotinas", (TreinoService s, CancellationToken ct) => s.RotinasAsync(ct));
        api.MapPost("/rotinas", (NovaRotinaDto dto, TreinoService s, CancellationToken ct) => s.CriarRotinaAsync(dto, ct));

        api.MapGet("/sessoes", (int? limite, TreinoService s, CancellationToken ct) => s.SessoesRecentesAsync(limite ?? 20, ct));
        api.MapPost("/sessoes", (NovaSessaoDto dto, TreinoService s, CancellationToken ct) => s.IniciarSessaoAsync(dto, ct));
        api.MapGet("/sessoes/{id:int}", (int id, TreinoService s, CancellationToken ct) => s.SessaoAsync(id, ct));
        api.MapPost("/sessoes/{id:int}/series", (int id, NovaSerieDto dto, TreinoService s, CancellationToken ct) => s.RegistrarSerieAsync(id, dto, ct));
        api.MapDelete("/sessoes/{id:int}/series/{serieId:int}", async (int id, int serieId, TreinoService s, CancellationToken ct) =>
        {
            await s.RemoverSerieAsync(id, serieId, ct);
            return Results.NoContent();
        });

        api.MapGet("/evolucao/semana", (TreinoService s, CancellationToken ct) => s.SemanaAsync(ct));
        api.MapGet("/evolucao/{aparelhoId:int}", (int aparelhoId, TreinoService s, CancellationToken ct) => s.EvolucaoAsync(aparelhoId, ct));
    }

    /// <summary>
    /// Lê multipart/form-data seção a seção. O arquivo chega como Stream direto da requisição
    /// (sem buffer em disco do IFormFile) e sai de escopo ao fim do callback.
    /// </summary>
    private static async Task LerMultipartAsync(HttpRequest req, CancellationToken ct, Action<string, string> campo, Func<Stream, Task> arquivo)
    {
        if (!MediaTypeHeaderValue.TryParse(req.ContentType, out var mt) || !mt.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new ValidacaoException("Envie multipart/form-data.");
        var boundary = HeaderUtilities.RemoveQuotes(mt.Boundary).Value
            ?? throw new ValidacaoException("Multipart sem boundary.");

        var reader = new MultipartReader(boundary, req.Body) { BodyLengthLimit = 16 * 1024 * 1024 };
        var recebeuArquivo = false;
        while (await reader.ReadNextSectionAsync(ct) is { } secao)
        {
            if (!ContentDispositionHeaderValue.TryParse(secao.ContentDisposition, out var cd)) continue;
            if (cd.IsFileDisposition())
            {
                if (recebeuArquivo) continue;
                recebeuArquivo = true;
                await arquivo(secao.Body);
            }
            else if (cd.IsFormDisposition())
            {
                using var sr = new StreamReader(secao.Body);
                var valor = await sr.ReadToEndAsync(ct);
                if (valor.Length > 2000) throw new ValidacaoException("Campo de texto muito longo.");
                campo(cd.Name.Value?.Trim('"') ?? "", valor);
            }
        }
    }
}

/// <summary>Valida DataAnnotations dos DTOs de entrada (inclusive listas aninhadas) e devolve 400.</summary>
public class ValidacaoFiltro : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        foreach (var arg in ctx.Arguments)
        {
            if (arg is null || !EhDto(arg.GetType())) continue;
            var erros = new List<ValidationResult>();
            if (!Validar(arg, erros))
                return Results.ValidationProblem(erros
                    .GroupBy(e => e.MemberNames.FirstOrDefault() ?? "")
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "inválido").ToArray()));
        }
        return await next(ctx);
    }

    private static bool EhDto(Type t) => t.Namespace == typeof(PerfilDto).Namespace && t.IsClass;

    private static bool Validar(object obj, List<ValidationResult> erros)
    {
        var ok = Validator.TryValidateObject(obj, new ValidationContext(obj), erros, true);
        foreach (var p in obj.GetType().GetProperties())
        {
            var v = p.GetValue(obj);
            if (v is string || v is null) continue;
            if (v is System.Collections.IEnumerable lista)
            {
                foreach (var item in lista)
                    if (item != null && EhDto(item.GetType())) ok &= Validar(item, erros);
            }
            else if (EhDto(v.GetType())) ok &= Validar(v, erros);
        }
        return ok;
    }
}
