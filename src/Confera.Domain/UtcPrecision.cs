namespace Confera.Domain;

public static class UtcPrecision
{
    public static DateTime Floor(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Date and time must be in UTC.", nameof(utc));
        }

        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }
}
