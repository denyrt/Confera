using Confera.Application.Bookings;
using Microsoft.AspNetCore.Mvc;

namespace Confera.Api.Bookings;

[ApiController]
[Route("bookings")]
public sealed class BookingsController(CreateBookingService service) : ControllerBase
{
    [HttpPost]
    [EndpointSummary("Book a conference room with a recorded price breakdown")]
    [EndpointDescription("Start and end require ISO 8601 timestamps with Z or an explicit offset and whole-microsecond precision. "
        + "They are normalized to UTC. Duration is 30 minutes to 24 hours with full tariff coverage; the start cannot be in the past. "
        + "serviceIds is required and may be empty. Selected services are charged once; all prices are recorded in UAH. "
        + "Adjacent bookings are allowed. Repeated requests are not idempotent; a lost response does not prove the booking was not saved.")]
    [ProducesResponseType<BookingResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BookingResult>> Create(CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(new CreateBookingCommand(
            request.RoomId, request.Start.UtcDateTime, request.End.UtcDateTime, request.ServiceIds), cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }
}
