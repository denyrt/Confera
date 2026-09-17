using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Confera.Api.Errors;

internal static class ApiProblems
{
    internal static ProblemDetails Create(HttpContext context, int status, string code, string detail) => new()
    {
        Status = status,
        Type = "about:blank",
        Title = ReasonPhrases.GetReasonPhrase(status),
        Detail = detail,
        Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier }
    };

    internal static IActionResult InvalidRequest(ActionContext context)
    {
        var problem = Create(context.HttpContext, StatusCodes.Status400BadRequest, "invalid_request",
            "Supply all required fields with valid values and formats as described by the endpoint contract.");
        var response = new BadRequestObjectResult(problem);
        response.ContentTypes.Add("application/problem+json");
        return response;
    }

    internal static ObjectResult Response(HttpContext context, int status, string code, string detail)
    {
        // Preserve the response metadata previously supplied by exception middleware
        // and its ProblemDetails writer for expected failures.
        context.Response.Headers.CacheControl = "no-cache,no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "-1";
        context.Response.Headers.Remove("ETag");

        var problem = Create(context, status, code, detail);
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }
}
