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
}
