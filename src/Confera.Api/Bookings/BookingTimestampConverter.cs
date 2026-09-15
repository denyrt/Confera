using System.Text.Json;
using System.Text.Json.Serialization;

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

        var text = reader.GetString()!;
        var offsetStart = text.EndsWith('Z') ? text.Length - 1 : text.Length - 6;
        var hasOffset = text.EndsWith('Z')
            || (offsetStart > 0 && text[offsetStart] is '+' or '-' && text[^3] == ':');
        if (!hasOffset || !reader.TryGetDateTimeOffset(out var value))
        {
            throw new JsonException("Supply an ISO 8601 timestamp with Z or an explicit UTC offset.");
        }

        var decimalPoint = text.IndexOf('.');
        if (decimalPoint >= 0)
        {
            var fraction = text.AsSpan(decimalPoint + 1, offsetStart - decimalPoint - 1);
            // Parsing accepts up to 16 fractional digits but retains only the first seven.
            // Even digits beyond that limit must not hide a nonzero sub-microsecond value.
            for (var i = 6; i < fraction.Length; i++)
            {
                if (fraction[i] != '0')
                {
                    throw new JsonException("Timestamp precision must be whole microseconds.");
                }
            }
        }

        return value.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime());
}
