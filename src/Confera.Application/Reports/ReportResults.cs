namespace Confera.Application.Reports;

public sealed record RoomReport(DateTime Start, DateTime End, string Currency, IReadOnlyList<RoomReportRow> Items);

public sealed record RoomReportRow(Guid RoomId, string RoomName, long BookingCount, decimal TotalBookedSeconds,
    decimal RentalValue, decimal ServiceValue, decimal TotalValue);

public sealed record ServiceReport(DateTime Start, DateTime End, string Currency, IReadOnlyList<ServiceReportRow> Items);

public sealed record ServiceReportRow(string ServiceName, long SelectionCount, decimal TotalValue);
