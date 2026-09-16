using System.Text.Json;
using Confera.Api.Bookings;

namespace Confera.Integration.Tests;

public sealed class BookingTimestampTests
{
    [Theory]
    [InlineData("2030-01-15T11:00Z")]
    [InlineData("2030-01-15T11:00:00Z")]
    [InlineData("2030-01-15T13:00:00+02:00")]
    [InlineData("2030-01-15T11:00:00-00:00")]
    [InlineData("2030-01-15T11:00:00.1234560000000000Z")]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("9999-12-31T23:59:59.999999Z")]
    public void SharedParserPreservesSystemTextJsonInstants(string text)
    {
        var json = JsonSerializer.Serialize(text);
        using var document = JsonDocument.Parse(json);
        var expected = document.RootElement.GetDateTimeOffset().ToUniversalTime();
        var actual = JsonSerializer.Deserialize<DateTimeOffset>(json, Options());
        Assert.Equal(expected, actual);
        Assert.Equal(TimeSpan.Zero, actual.Offset);
    }

    [Theory]
    [InlineData("2030-01-15T11:00:00.1234560000000001Z")]
    [InlineData("2030-01-15T11:00:00.0000001Z")]
    [InlineData("2030-01-15T11:00:00.00000000000000000Z")]
    [InlineData("2030-01-15T11:00:00")]
    [InlineData("2030-01-15 11:00:00Z")]
    [InlineData("2030-01-15t11:00:00z")]
    [InlineData("2030-02-30T11:00:00Z")]
    public void SharedParserRejectsInvalidFormatsAndPrecision(string text)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTimeOffset>(JsonSerializer.Serialize(text), Options()));
    }

    private static JsonSerializerOptions Options() => new() { Converters = { new BookingTimestampConverter() } };
}
