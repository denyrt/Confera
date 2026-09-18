using Confera.Application.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Confera.Infrastructure.Persistence;

public sealed class ReportReader(
    IDbContextFactory<ConferaDbContext> factory, ILogger<ReportReader> logger) : IReportReader
{
    public Task<IReadOnlyList<RoomReportRow>> GetRoomsAsync(ReportPeriod period, CancellationToken cancellationToken) =>
        TranslateErrorsAsync<IReadOnlyList<RoomReportRow>>(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            // Each child aggregate produces one value per booking, avoiding segment/service multiplication.
            // EXTRACT returns numeric seconds, preserving microseconds without floating-point conversion.
            return await db.Database.SqlQuery<RoomReportRow>($"""
                WITH selected_bookings AS (
                    SELECT b."RoomId", b."EndsAtUtc" - b."StartsAtUtc" AS duration,
                        (SELECT SUM(p."Price") FROM "BookingPriceSegments" p
                         WHERE p."BookingId" = b."Id") AS rental_value,
                        COALESCE((SELECT SUM(s."ServicePriceSnapshot") FROM "BookedRoomServiceSnapshots" s
                                  WHERE s."BookingId" = b."Id"), 0) AS service_value
                    FROM "Bookings" b
                    WHERE b."StartsAtUtc" >= {period.StartsAtUtc} AND b."StartsAtUtc" < {period.EndsAtUtc}
                )
                SELECT r."Id" AS "RoomId", r."Name" AS "RoomName", COUNT(*) AS "BookingCount",
                    SUM(EXTRACT(EPOCH FROM b.duration)) AS "TotalBookedSeconds",
                    SUM(b.rental_value) AS "RentalValue", SUM(b.service_value) AS "ServiceValue",
                    SUM(b.rental_value + b.service_value) AS "TotalValue"
                FROM selected_bookings b
                JOIN "Rooms" r ON r."Id" = b."RoomId"
                GROUP BY r."Id", r."Name"
                ORDER BY "TotalValue" DESC, r."Id"
                """).ToListAsync(cancellationToken);
        }, cancellationToken);

    public Task<IReadOnlyList<ServiceReportRow>> GetServicesAsync(ReportPeriod period, CancellationToken cancellationToken) =>
        TranslateErrorsAsync<IReadOnlyList<ServiceReportRow>>(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            return await db.Database.SqlQuery<ServiceReportRow>($"""
                SELECT s."ServiceNameSnapshot" COLLATE "C" AS "ServiceName",
                    COUNT(*) AS "SelectionCount", SUM(s."ServicePriceSnapshot") AS "TotalValue"
                FROM "BookedRoomServiceSnapshots" s
                JOIN "Bookings" b ON b."Id" = s."BookingId"
                WHERE b."StartsAtUtc" >= {period.StartsAtUtc} AND b."StartsAtUtc" < {period.EndsAtUtc}
                GROUP BY s."ServiceNameSnapshot" COLLATE "C"
                ORDER BY "SelectionCount" DESC, "ServiceName"
                """).ToListAsync(cancellationToken);
        }, cancellationToken);

    private async Task<T> TranslateErrorsAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested
            && PersistenceErrors.IsUnavailable(PersistenceErrors.Unwrap(error)))
        {
            logger.LogError(error, "Report persistence is temporarily unavailable.");
            throw new ReportOperationException(ReportFailure.PersistenceUnavailable, error);
        }
    }
}
