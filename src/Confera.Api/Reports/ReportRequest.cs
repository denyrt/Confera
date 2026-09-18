using System.ComponentModel.DataAnnotations;
using Confera.Api.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Confera.Api.Reports;

public sealed class ReportRequest
{
    [FromQuery(Name = "start"), Required, BindRequired]
    public string Start { get; init; } = null!;

    [FromQuery(Name = "end"), Required, BindRequired]
    public string End { get; init; } = null!;

    internal bool TryGetUtcPeriod(out DateTime startsAtUtc, out DateTime endsAtUtc)
    {
        startsAtUtc = default;
        endsAtUtc = default;
        if (!ApiTimestamp.TryParse(Start, out var start) || !ApiTimestamp.TryParse(End, out var end))
        {
            return false;
        }

        startsAtUtc = start.UtcDateTime;
        endsAtUtc = end.UtcDateTime;
        return true;
    }
}
