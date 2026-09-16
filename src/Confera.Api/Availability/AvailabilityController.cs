using System.ComponentModel.DataAnnotations;
using Confera.Application.Availability;
using Microsoft.AspNetCore.Mvc;

namespace Confera.Api.Availability;

[ApiController]
[Route("rooms/availability")]
public sealed class AvailabilityController(SearchAvailabilityService service) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("Search available rooms with their current services")]
    [EndpointDescription("start and end require ISO 8601 timestamps with Z or an explicit offset and whole-microsecond precision. "
        + "Encode a positive offset's + as %2B. Duration is 30 minutes to 24 hours; the start cannot be in the past. "
        + "capacity is the required minimum. page defaults to 1, pageSize to 20 (maximum 100). "
        + "Rooms are ordered by capacity then ID; hasNextPage indicates another result at read time. "
        + "Missing tariff coverage or no matches returns an empty page. Rates and services are current UAH prices, not a period quote. "
        + "Search does not reserve rooms; results may change between pages and booking rechecks availability. Responses use no-store.")]
    [ProducesResponseType<AvailabilityPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AvailabilityPage>> Get([FromQuery] AvailabilityRequest request, CancellationToken cancellationToken,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, AvailabilityQuery.MaximumPageSize)] int pageSize = 20)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.SearchAsync(request.ToQuery(page, pageSize), cancellationToken));
    }
}
