using Confera.Api.Errors;
using Confera.Application.Rooms;
using Microsoft.AspNetCore.Mvc;

namespace Confera.Api.Rooms;

[ApiController]
[Route("rooms")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class RoomsController(RoomManagementService service) : ControllerBase
{
    [HttpPost]
    [EndpointSummary("Create a room and its current service offerings")]
    [EndpointDescription("All fields are required. Names are at most 64 UTF-16 code units; active room names and room-specific service names "
        + "are unique ignoring case and surrounding whitespace. Capacity is positive. Hourly rate is 1000-100000 UAH and service prices "
        + "are 200-20000 UAH, with at most three fractional digits. services may be empty. Location identifies the room read endpoint.")]
    [ProducesResponseType<RoomResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoomResult>> Create(RoomRequest request, CancellationToken cancellationToken)
    {
        if (!request.TryToCommand(out var command))
        {
            return MapFailure(new RoomError.InvalidRequest());
        }

        var result = await service.CreateAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return MapFailure(result.Error);
        }

        return CreatedAtAction(nameof(Get), new { id = result.Value.Room.Id }, result.Value.Room);
    }

    [HttpGet("{id}")]
    [EndpointSummary("Read an active room and its current ETag")]
    [EndpointDescription("Returns current room data and services with a strong ETag response header. Copy that header into If-Match "
        + "for PUT or DELETE. Read again after an update or a 412 response. Unknown and deleted rooms return 404. Responses use Cache-Control: no-store.")]
    [ProducesResponseType<RoomResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomResult>> Get(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await service.GetAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            return MapFailure(result.Error);
        }

        Response.Headers.ETag = RoomEntityTags.Format(result.Value.Version);
        return Ok(result.Value.Room);
    }

    [HttpPut("{id}")]
    [EndpointSummary("Replace room details if its version still matches")]
    [EndpointDescription("Supply every editable field and the complete service array. Omitted offerings are removed; [] removes all. "
        + "Matching normalized service names retain IDs and accept spelling/price changes; renamed offerings receive new IDs. "
        + "Capacity reduction requires no ongoing/future bookings. If-Match requires explicit strong version tags from GET; lists are supported, "
        + "wildcard * is rejected, weak tags do not match. Missing header: 428; stale version: 412. "
        + "All changes are atomic; historical bookings stay unchanged. No-op updates retain the version. "
        + "The response contains current data without an ETag; GET returns the next validator. Writes are not automatically retried.")]
    [ProducesResponseType<RoomResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<RoomResult>> Update(Guid id, RoomRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken)
    {
        // Read raw values rather than the binder's first value to preserve repeated header fields.
        if (!RoomEntityTags.TryParse(Request.Headers, out var versions)
            || !request.TryToCommand(out var command))
        {
            return MapFailure(new RoomError.InvalidRequest());
        }

        var result = await service.UpdateAsync(id, command, versions, cancellationToken);
        if (!result.IsSuccess)
        {
            return MapFailure(result.Error);
        }

        return Ok(result.Value.Room);
    }

    [HttpDelete("{id}")]
    [EndpointSummary("Soft-delete a room if no unfinished bookings remain")]
    [EndpointDescription("An active room requires If-Match from GET and no booking ending after the current UTC time. "
        + "Missing header: 428; stale version: 412; wildcard * is rejected. Services and booking history are retained. "
        + "An already deleted room returns 204 even with an old or missing If-Match; malformed headers still return 400. Unknown IDs return 404.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Delete(Guid id, [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        if (!RoomEntityTags.TryParse(Request.Headers, out var versions))
        {
            return MapFailure(new RoomError.InvalidRequest());
        }

        var result = await service.DeleteAsync(id, versions, cancellationToken);
        if (!result.IsSuccess)
        {
            return MapFailure(result.Error);
        }

        return NoContent();
    }

    private ObjectResult MapFailure(RoomError error)
    {
        var (status, code, detail) = error switch
        {
            RoomError.InvalidRequest =>
                (400, "invalid_request", "Supply a nonempty room ID and valid explicit entity tags in If-Match; wildcard * is not supported."),
            RoomError.InvalidData invalid => (400, "invalid_room_data", invalid.Description),
            RoomError.NotFound => (404, "room_not_found", "The room was not found."),
            RoomError.NameConflict =>
                (409, "room_name_conflict", "An active room already uses this name."),
            RoomError.HasUnfinishedBookings =>
                (409, "room_has_unfinished_bookings", "The room has ongoing or future bookings; capacity reduction and deletion are unavailable."),
            RoomError.VersionMismatch =>
                (412, "room_version_mismatch", "The room has changed. Read it again and review your changes before submitting the new ETag in If-Match."),
            RoomError.PreconditionRequired =>
                (428, "room_precondition_required", "Read the room and supply its ETag in the If-Match header."),
            RoomError.PersistenceUnavailable =>
                (503, "room_persistence_unavailable", "The room operation could not be confirmed. A failed response does not prove that no change was saved."),
            _ => throw new InvalidOperationException("The room error has no HTTP mapping.")
        };

        return ApiProblems.Response(HttpContext, status, code, detail);
    }
}
