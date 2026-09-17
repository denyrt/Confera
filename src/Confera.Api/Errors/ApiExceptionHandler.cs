using Microsoft.AspNetCore.Diagnostics;

namespace Confera.Api.Errors;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        logger.LogError(exception, "Request {TraceId} failed with {Code}.", context.TraceIdentifier, "internal_error");

        await Results.Problem(ApiProblems.Create(context, StatusCodes.Status500InternalServerError,
            "internal_error", "The request could not be completed.")).ExecuteAsync(context);
        return true;
    }
}
