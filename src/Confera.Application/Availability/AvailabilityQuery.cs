namespace Confera.Application.Availability;

public sealed record AvailabilityQuery(DateTime StartsAtUtc, DateTime EndsAtUtc, int Capacity,
    int Page = 1, int PageSize = 20)
{
    public const int MaximumPageSize = 100;
}
