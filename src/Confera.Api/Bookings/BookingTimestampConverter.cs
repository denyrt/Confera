using System.Text.Json;
using System.Text.Json.Serialization;
using Confera.Api.Time;

namespace Confera.Api.Bookings;

/// <summary>Validates the original JSON text before DateTimeOffset parsing can discard precision.</summary>
public sealed class BookingTimestampConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Supply an ISO 8601 timestamp with an explicit offset.");
        }

        if (!ApiTimestamp.TryParse(reader.GetString(), out var value))
        {
            throw new JsonException("Supply an ISO 8601 timestamp with an explicit offset and whole-microsecond precision.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime());
}
