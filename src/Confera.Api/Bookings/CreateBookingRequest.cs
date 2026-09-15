using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Confera.Api.Bookings;

public sealed class CreateBookingRequest
{
    public required Guid RoomId { get; init; }

    [JsonConverter(typeof(BookingTimestampConverter))]
    public required DateTimeOffset Start { get; init; }

    [JsonConverter(typeof(BookingTimestampConverter))]
    public required DateTimeOffset End { get; init; }

    [Required]
    public required Guid[] ServiceIds { get; init; }
}
