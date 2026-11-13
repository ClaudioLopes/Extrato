using Extrato.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Extrato.Api;

public class TratadorErros(IProblemDetailsService problemas, ILogger<TratadorErros> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, titulo) = ex switch
        {
            NaoEncontradoException => (StatusCodes.Status404NotFound, "Não encontrado"),
            ValidacaoException => (StatusCodes.Status400BadRequest, "Requisição inválida"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Requisição inválida"),
            _ => (StatusCodes.Status500InternalServerError, "Erro interno")
        };
        if (status == 500) logger.LogError(ex, "Erro não tratado");

        ctx.Response.StatusCode = status;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = titulo,
                Detail = status == 500 ? "Ocorreu um erro inesperado." : ex.Message
            }
        });
    }
}
