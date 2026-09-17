using System.ComponentModel.DataAnnotations;
using Confera.Api.Time;
using Confera.Application.Reports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Confera.Api.Reports;

public sealed class ReportRequest
{
    [FromQuery(Name = "start"), Required, BindRequired]
    public string Start { get; init; } = null!;

    [FromQuery(Name = "end"), Required, BindRequired]
    public string End { get; init; } = null!;

    internal (DateTime Start, DateTime End) ToUtc()
    {
        if (!ApiTimestamp.TryParse(Start, out var start) || !ApiTimestamp.TryParse(End, out var end))
        {
            throw new ReportOperationException(ReportFailure.InvalidRequest);
        }

        return (start.UtcDateTime, end.UtcDateTime);
    }
}
