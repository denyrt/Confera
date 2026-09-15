using System.Text;
using Confera.Domain;
using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Confera.Integration.Tests;

public sealed class IntegrityTests(PostgresFixture postgres)
{
    private const string EmptyId = "00000000-0000-0000-0000-000000000000";

    [Fact]
    public async Task SqlValueChecksRejectInvalidInputsAndAcceptInclusiveBounds()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using (var db = database.Context())
        {
            var room = new Room("Room", 1, 1000.001m);
            room.SetServices([new("Service", 200.001m)]);
            var start = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);
            var rule = new BookingPricingRule("Day", "Day", new TimeOnly(9, 0), new TimeOnly(18, 0), 1m, 0);
            db.Add(room);
            db.Add(rule);
            db.Add(room.Book(start, start.AddMinutes(30), start, room.Services.Select(x => x.Id).ToArray(), [rule]));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (string Table, string Column, string Min, string Max, string Below, string Above, string Extra)[] values =
        [
            ("Rooms", "HourlyRate", "1000", "100000", "999.999", "100000.001", "1000.0001"),
            ("RoomServices", "Price", "200", "20000", "199.999", "20000.001", "200.0001"),
            ("BookingPricingRules", "Multiplier", "0.5", "2", "0.49", "2.01", "1.001"),
            ("Bookings", "HourlyRateSnapshot", "1000", "100000", "999", "100001", "1000.0001"),
            ("Bookings", "TotalPrice", "0", "999999999999999.999", "-0.001", "1000000000000000", "1.0001"),
            ("BookedRoomServiceSnapshots", "ServicePriceSnapshot", "200", "20000", "199", "20001", "200.0001"),
            ("BookingPriceSegments", "HourlyRateSnapshot", "1000", "100000", "999", "100001", "1000.0001"),
            ("BookingPriceSegments", "MultiplierSnapshot", "0.5", "2", "0.49", "2.01", "1.001"),
            ("BookingPriceSegments", "Price", "0", "4800000", "-0.001", "4800000.001", "1.0001")
        ];
        foreach (var value in values)
        {
            foreach (var invalid in new[] { value.Below, value.Above, value.Extra, "'NaN'", "'Infinity'", "'-Infinity'" })
            {
                await RejectAsync(database, $"UPDATE \"{value.Table}\" SET \"{value.Column}\"={invalid}",
                    PostgresErrorCodes.CheckViolation, $"CK_{value.Table}_{value.Column}");
            }

            foreach (var valid in new[] { value.Min, value.Max, value.Min + "::numeric + 0.0000" })
            {
                await database.SqlAsync($"UPDATE \"{value.Table}\" SET \"{value.Column}\"={valid}");
            }
            await RejectAsync(database, $"UPDATE \"{value.Table}\" SET \"{value.Column}\"=NULL", PostgresErrorCodes.NotNullViolation);
        }

        foreach (var table in new[] { "Rooms", "RoomServices", "BookingPricingRules", "Bookings", "BookedRoomServiceSnapshots", "BookingPriceSegments" })
        {
            await RejectAsync(database, $"UPDATE \"{table}\" SET \"Id\"='{EmptyId}'", PostgresErrorCodes.CheckViolation, $"CK_{table}_Id");
        }
        foreach (var (table, column) in new[] { ("Rooms", "Name"), ("RoomServices", "Name"), ("BookingPricingRules", "Code"), ("BookingPricingRules", "Name"), ("BookedRoomServiceSnapshots", "ServiceNameSnapshot"), ("BookingPriceSegments", "PricingCodeSnapshot") })
        {
            foreach (var invalid in new[] { "''", "U&'\\00A0\\2009'", "repeat('a',65)", "repeat(U&'\\+01F600',33)" })
            {
                await RejectAsync(database, $"UPDATE \"{table}\" SET \"{column}\"={invalid}", PostgresErrorCodes.CheckViolation, $"CK_{table}_{column}");
            }
            await database.SqlAsync($"UPDATE \"{table}\" SET \"{column}\"=repeat(U&'\\+01F600',32)");
        }
        await RejectAsync(database, "UPDATE \"Rooms\" SET \"Capacity\"=0", PostgresErrorCodes.CheckViolation, "CK_Rooms_Capacity");
        foreach (var (table, column) in new[] { ("RoomServices", "RoomId"), ("Bookings", "RoomId"), ("BookedRoomServiceSnapshots", "BookingId"), ("BookingPriceSegments", "BookingId") })
        {
            await RejectAsync(database, $"UPDATE \"{table}\" SET \"{column}\"=gen_random_uuid()", PostgresErrorCodes.ForeignKeyViolation);
        }
        foreach (var (table, column) in new[] { ("Bookings", "StartsAtUtc"), ("Bookings", "EndsAtUtc"), ("Bookings", "CreatedAtUtc"), ("BookingPriceSegments", "StartsAtUtc"), ("BookingPriceSegments", "EndsAtUtc") })
        {
            foreach (var invalid in new[] { "infinity", "-infinity", "0001-01-01 00:00:00 BC", "10000-01-01 00:00:00+00" })
            {
                await RejectAsync(database, $"UPDATE \"{table}\" SET \"{column}\"='{invalid}'", PostgresErrorCodes.CheckViolation);
            }
        }
        await database.SqlAsync("UPDATE \"Bookings\" SET \"CreatedAtUtc\"='0001-01-01 00:00:00+00'");
        await database.SqlAsync("UPDATE \"Bookings\" SET \"CreatedAtUtc\"='9999-12-31 23:59:59.999999+00'");
        foreach (var duration in new[] { "0", "29 minutes 59.999999 seconds", "24 hours 0.000001 seconds", "-1 hour" })
        {
            await RejectAsync(database, $"UPDATE \"Bookings\" SET \"EndsAtUtc\"=\"StartsAtUtc\"+interval '{duration}'", PostgresErrorCodes.CheckViolation, "CK_Bookings_Duration");
        }
        await database.SqlAsync("UPDATE \"Bookings\" SET \"EndsAtUtc\"=\"StartsAtUtc\"+interval '24 hours'");
        await RejectAsync(database, "UPDATE \"BookingPriceSegments\" SET \"EndsAtUtc\"=\"StartsAtUtc\"", PostgresErrorCodes.CheckViolation, "CK_BookingPriceSegments_Duration");
        await database.SqlAsync("UPDATE \"BookingPriceSegments\" SET \"EndsAtUtc\"=\"StartsAtUtc\"+interval '0.000001 seconds'");
        await RejectAsync(database, "UPDATE \"BookingPricingRules\" SET \"StartsAt\"='24:00'", PostgresErrorCodes.CheckViolation, "CK_BookingPricingRules_DailyInterval");
        await RejectAsync(database, "UPDATE \"BookingPricingRules\" SET \"EndsAt\"='24:00'", PostgresErrorCodes.CheckViolation, "CK_BookingPricingRules_DailyInterval");
        await RejectAsync(database, "UPDATE \"BookingPricingRules\" SET \"EndsAt\"=\"StartsAt\"", PostgresErrorCodes.CheckViolation, "CK_BookingPricingRules_DailyInterval");
        await database.SqlAsync("UPDATE \"BookingPricingRules\" SET \"StartsAt\"='23:59:59.999999', \"EndsAt\"='00:00'");
    }

    [Fact]
    public async Task NameKeysConformForEveryUnicodeScalarAndPreventBypass()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var batch = new List<string>(8192);
        var mismatches = new List<string>();
        async Task CompareAsync()
        {
            await using var command = new NpgsqlCommand("SELECT value, confera_name_key(value) FROM unnest(@values::text[]) value", connection);
            command.Parameters.AddWithValue("values", batch.ToArray());
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                var input = reader.GetString(0);
                var expected = reader.GetString(1);
                if (NameIdentity.Key(input) != expected && mismatches.Count < 100)
                {
                    mismatches.Add($"U+{Rune.GetRuneAt(input, 0).Value:X}: .NET={NameIdentity.Key(input)} PG={expected}");
                }
            }
            batch.Clear();
        }
        for (var scalar = 1; scalar <= 0x10FFFF; scalar++)
        {
            if (!Rune.IsValid(scalar)) continue;
            batch.Add(new Rune(scalar).ToString());
            if (batch.Count == 8192) await CompareAsync();
        }
        if (batch.Count > 0) await CompareAsync();
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));

        foreach (var name in new[] { "\u00a0 Україна їєґ \u2009", " Latin Café ", "Σςσ", "İıi", "ßẞ", "𐐨😀", "A А", "é e\u0301", "two  spaces" })
        {
            await using var command = new NpgsqlCommand("SELECT confera_name_key(@name)", connection);
            command.Parameters.AddWithValue("name", name);
            Assert.Equal(NameIdentity.Key(name), (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
        }

        await database.SqlAsync(RoomSql(" Україна "));
        await RejectAsync(database, RoomSql("україна"), PostgresErrorCodes.UniqueViolation, "UX_Rooms_ActiveName");
        await RejectAsync(database, "INSERT INTO \"Rooms\" (\"Id\",\"Name\",\"NameKey\",\"Capacity\",\"HourlyRate\") VALUES (gen_random_uuid(),'x','forged',1,1000)", "428C9");
        await database.SqlAsync("UPDATE \"Rooms\" SET \"IsDeleted\"=true");
        await database.SqlAsync(RoomSql("україна"));
        await database.SqlAsync("INSERT INTO \"RoomServices\" (\"Id\",\"RoomId\",\"Name\",\"Price\") SELECT gen_random_uuid(),\"Id\",'Wi-Fi',300 FROM \"Rooms\"");
        await RejectAsync(database, "INSERT INTO \"RoomServices\" (\"Id\",\"RoomId\",\"Name\",\"Price\") SELECT gen_random_uuid(),\"Id\",' wi-fi ',300 FROM \"Rooms\" LIMIT 1", PostgresErrorCodes.UniqueViolation, "UX_RoomServices_RoomName");
        await RejectAsync(database, "UPDATE \"RoomServices\" SET \"NameKey\"='forged'", "428C9");
    }

    [Fact]
    public async Task IndependentConcurrentDuplicateNamesHaveOneWinner()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await RaceAsync(database, RoomSql("Room"), RoomSql(" room "), PostgresErrorCodes.UniqueViolation);
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM \"Rooms\""));
    }

    [Fact]
    public async Task GlobalPriorityProtectsDisjointConcurrentWritesAndAllowsDistinctOverlaps()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        static string RuleSql(string start, string end, int priority) => $"INSERT INTO \"BookingPricingRules\" VALUES (gen_random_uuid(),'Rule','Rule','{start}','{end}',1,{priority})";
        await RaceAsync(database, RuleSql("06:00", "09:00", -5), RuleSql("20:00", "23:00", -5), PostgresErrorCodes.UniqueViolation);
        await RejectAsync(database, RuleSql("09:00", "18:00", -5), PostgresErrorCodes.UniqueViolation, "UQ_BookingPricingRules_Priority");
        await database.SqlAsync(RuleSql("09:00", "18:00", 100));
        await database.SqlAsync(RuleSql("12:00", "14:00", 101));
        await database.SqlAsync(RuleSql("22:00", "06:00", 102));
    }

    [Fact]
    public async Task BookingExclusionProtectsIndependentConnectionsAndRollsBackChildren()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await database.SqlAsync(RoomSql("Room"));
        var room = await database.ScalarAsync<Guid>("SELECT \"Id\" FROM \"Rooms\"");
        string BookingSql(Guid roomId, string start, string end) => $"INSERT INTO \"Bookings\" VALUES (gen_random_uuid(),'{roomId}','2026-09-15 {start}+00','2026-09-15 {end}+00','2026-09-15 09:00+00',1000,1000)";
        await RaceAsync(database, BookingSql(room, "10:00", "11:00"), BookingSql(room, "10:30", "11:30"), PostgresErrorCodes.ExclusionViolation);
        var end = await database.ScalarAsync<DateTime>("SELECT \"EndsAtUtc\" FROM \"Bookings\"");
        await using (var db = database.Context())
        {
            var loaded = await db.Rooms.Include(x => x.Services).SingleAsync(TestContext.Current.CancellationToken);
            loaded.SetServices([new("Service", 200m)]);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            var rule = new BookingPricingRule("Day", "Day", new TimeOnly(9, 0), new TimeOnly(18, 0), 1m, 0);
            var conflict = loaded.Book(end.AddMinutes(-30), end.AddMinutes(30), end.AddHours(-2), loaded.Services.Select(x => x.Id).ToArray(), [rule]);
            db.Add(conflict);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PostgresErrorCodes.ExclusionViolation, ((PostgresException)error.InnerException!).SqlState);
        }
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"BookingPriceSegments\""));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM \"BookedRoomServiceSnapshots\""));
        await RejectAsync(database, BookingSql(room, "10:45", "11:15"), PostgresErrorCodes.ExclusionViolation, "EX_Bookings_RoomPeriod");
        await database.SqlAsync($"INSERT INTO \"Bookings\" VALUES (gen_random_uuid(),'{room}','{end:O}'::timestamptz,'{end:O}'::timestamptz+interval '30 minutes','2026-09-15 09:00+00',1000,0)");
        await database.SqlAsync(RoomSql("Another"));
        var another = await database.ScalarAsync<Guid>("SELECT \"Id\" FROM \"Rooms\" WHERE \"Name\"='Another'");
        await database.SqlAsync(BookingSql(another, "10:00", "11:00"));
        await RaceAsync(database, BookingSql(room, "15:00", "16:00"), BookingSql(another, "15:00", "16:00"), "success");
        await RaceAsync(database, BookingSql(room, "17:00", "18:00"), BookingSql(room, "18:00", "19:00"), "success");
    }

    [Fact]
    public async Task ParallelDatabasesAreCleanAndDisposalDropsOnlyOwnedDatabase()
    {
        var databases = await Task.WhenAll(postgres.CreateDatabaseAsync(), postgres.CreateDatabaseAsync());
        await using var second = databases[1];
        Assert.NotEqual(databases[0].Name, second.Name);
        Assert.Equal(0L, await second.ScalarAsync<long>("SELECT count(*) FROM \"Rooms\""));
        await databases[0].SqlAsync(RoomSql("Only first"));
        Assert.Equal(0L, await second.ScalarAsync<long>("SELECT count(*) FROM \"Rooms\""));
        await databases[0].DisposeAsync();
        Assert.False(await second.ScalarAsync<bool>($"SELECT EXISTS(SELECT FROM pg_database WHERE datname='{databases[0].Name}')"));
        await second.SqlAsync(RoomSql("Still usable"));
    }

    private static string RoomSql(string name) => $"INSERT INTO \"Rooms\" (\"Id\",\"Name\",\"Capacity\",\"HourlyRate\") VALUES (gen_random_uuid(),'{name}',1,1000)";

    private static async Task RejectAsync(TestDatabase database, string sql, string state, string? constraint = null)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.SqlAsync(sql));
        Assert.Equal(state, error.SqlState);
        if (constraint is not null) Assert.Equal(constraint, error.ConstraintName);
    }

    private static async Task RaceAsync(TestDatabase database, string first, string second, string expectedState)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        var pids = new System.Collections.Concurrent.ConcurrentBag<int>();
        async Task<string> WriteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync(timeout.Token);
            pids.Add(connection.ProcessID);
            await using var transaction = await connection.BeginTransactionAsync(timeout.Token);
            if (Interlocked.Increment(ref arrivals) == 2) ready.SetResult();
            await ready.Task.WaitAsync(timeout.Token);
            try
            {
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                await command.ExecuteNonQueryAsync(timeout.Token);
                await transaction.CommitAsync(timeout.Token);
                return "success";
            }
            catch (PostgresException error)
            {
                return error.SqlState;
            }
        }
        var results = await Task.WhenAll(WriteAsync(first), WriteAsync(second));
        Assert.Equal(2, pids.Distinct().Count());
        if (expectedState == "success")
        {
            Assert.All(results, result => Assert.Equal("success", result));
        }
        else
        {
            Assert.Single(results, x => x == "success");
            Assert.Single(results, x => x == expectedState);
        }
    }
}
