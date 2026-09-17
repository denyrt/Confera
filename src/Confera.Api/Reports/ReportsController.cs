using Confera.Application.Reports;
using Microsoft.AspNetCore.Mvc;

namespace Confera.Api.Reports;

[ApiController]
[Route("reports")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
public sealed class ReportsController(ReportService service) : ControllerBase
{
    private const string PeriodDescription = "start and end require explicit-offset ISO 8601 timestamps with whole-microsecond precision; "
        + "encode a positive offset's + as %2B. end must follow start; past/future periods of any positive duration are allowed. "
        + "Select bookings starting within [start, end) and include each in full, even if it ends outside the period. "
        + "Values use recorded UAH prices and include deleted-room history; they describe bookings, not payments. "
        + "Return all groups without pagination; no matches returns empty items. Responses echo UTC start/end and currency and use no-store. ";

    [HttpGet("rooms")]
    [EndpointSummary("Summarize booking counts, duration and value by room")]
    [EndpointDescription(PeriodDescription
        + "Only rooms with selected bookings appear, grouped by ID with the retained room name. "
        + "bookingCount counts bookings once; totalBookedSeconds is decimal seconds preserving microseconds. "
        + "rentalValue and serviceValue sum recorded segments/services; totalValue is their sum. "
        + "Order by totalValue descending, then roomId ascending.")]
    [ProducesResponseType<RoomReport>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RoomReport>> GetRooms([FromQuery] ReportRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var (start, end) = request.ToUtc();
        return Ok(await service.GetRoomsAsync(start, end, cancellationToken));
    }

    [HttpGet("services")]
    [EndpointSummary("Summarize service selections and recorded value")]
    [EndpointDescription(PeriodDescription
        + "Group by exact recorded serviceName across rooms, case-sensitively; renamed offerings remain separate. "
        + "selectionCount counts snapshots and totalValue sums their recorded prices. "
        + "Order by selectionCount descending, then exact serviceName ascending using PostgreSQL C collation.")]
    [ProducesResponseType<ServiceReport>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceReport>> GetServices([FromQuery] ReportRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var (start, end) = request.ToUtc();
        return Ok(await service.GetServicesAsync(start, end, cancellationToken));
    }
}
