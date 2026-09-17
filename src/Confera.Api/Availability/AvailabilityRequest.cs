using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Confera.Api.Time;
using Confera.Application.Availability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Confera.Api.Availability;

public sealed class AvailabilityRequest
{
    [FromQuery(Name = "start"), Required, BindRequired]
    public string Start { get; init; } = null!;

    [FromQuery(Name = "end"), Required, BindRequired]
    public string End { get; init; } = null!;

    [FromQuery(Name = "capacity"), Required, BindRequired, Range(1, int.MaxValue)]
    public int Capacity { get; init; }

    internal bool TryToQuery(int page, int pageSize, [NotNullWhen(true)] out AvailabilityQuery? query)
    {
        query = null;
        if (!ApiTimestamp.TryParse(Start, out var start) || !ApiTimestamp.TryParse(End, out var end))
        {
            return false;
        }

        query = new AvailabilityQuery(start.UtcDateTime, end.UtcDateTime, Capacity, page, pageSize);
        return true;
    }
}
