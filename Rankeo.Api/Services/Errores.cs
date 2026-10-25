using Microsoft.AspNetCore.Diagnostics;

namespace Rankeo.Api.Services;

/// <summary>Regla de negocio incumplida → 400 con el mensaje de la demo.</summary>
public class ReglaException(string mensaje) : Exception(mensaje);

/// <summary>Recurso inexistente → 404.</summary>
public class NoEncontradoException(string mensaje) : Exception(mensaje);

// Traduce las dos excepciones de negocio a ProblemDetails; el resto sigue como 500.
public class ErroresHandler(IProblemDetailsService problemas) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception error, CancellationToken ct)
    {
        int? status = error switch
        {
            ReglaException => StatusCodes.Status400BadRequest,
            NoEncontradoException => StatusCodes.Status404NotFound,
            _ => null,
        };
        if (status is null) return false;

        http.Response.StatusCode = status.Value;
        return await problemas.TryWriteAsync(new()
        {
            HttpContext = http,
            Exception = error,
            ProblemDetails = { Status = status, Detail = error.Message },
        });
    }
}
