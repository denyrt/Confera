using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Confera.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Confera.AppHost.Tests;

public sealed class OrchestrationTests
{
    [Fact]
    public async Task FreshStartupMigratesAndSeedsBeforeHealthyApiWithoutRestart()
    {
        using var timeout = Deadline();
        await using var builder = await CreateBuilderAsync(timeout.Token);
        Assert.Empty(Postgres(builder).Annotations.OfType<ContainerMountAnnotation>());
        await using var app = await builder.BuildAsync(timeout.Token);
        await using var diagnostics = new AppHostDiagnostics(app);
        var states = new ConcurrentQueue<ResourceEvent>();
        using var watchStop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var watch = RecordAsync(app, states, watchStop.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("confera-api", timeout.Token);
        using var client = app.CreateHttpClient("confera-api", "http");
        Assert.True((await client.GetAsync("/health", timeout.Token)).IsSuccessStatusCode);
        var connection = await app.GetConnectionStringAsync("confera", timeout.Token);
        await using var db = new ConferaDbContext(PersistenceConfiguration.Options(connection!));
        Assert.Equal(3, await db.Rooms.CountAsync(timeout.Token));
        Assert.Equal(4, await db.PricingRules.CountAsync(timeout.Token));
        Assert.Single(await db.InitializationMarkers.ToListAsync(timeout.Token));
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync(timeout.Token));
        await app.ResourceNotifications.WaitForResourceAsync("confera-migrations", e => e.Snapshot.ExitCode == 0, timeout.Token);
        var snapshots = states.ToArray();
        var completed = Array.FindIndex(snapshots, e => e.Resource.Name == "confera-migrations" && e.Snapshot.ExitCode == 0);
        var running = Array.FindIndex(snapshots, e => e.Resource.Name == "confera-api" && e.Snapshot.State?.Text == KnownResourceStates.Running);
        Assert.True(completed >= 0 && running > completed, "API must run after successful worker completion.");
        watchStop.Cancel();
        await watch;
    }

    [Fact]
    public async Task FailedMigrationHasNonzeroWorkerResultAndBlocksApi()
    {
        using var timeout = Deadline();
        await using var builder = await CreateBuilderAsync(timeout.Token);
        var worker = builder.Resources.OfType<ProjectResource>().Single(x => x.Name == "confera-migrations");
        var database = builder.Resources.OfType<PostgresDatabaseResource>().Single();
        builder.CreateResourceBuilder(worker).WithEnvironment(ctx =>
            ctx.EnvironmentVariables["ConnectionStrings__confera"] = ReferenceExpression.Create($"{database.ConnectionStringExpression};Search Path=p1_missing_schema"));
        await using var app = await builder.BuildAsync(timeout.Token);
        await using var diagnostics = new AppHostDiagnostics(app);
        var states = new ConcurrentQueue<ResourceEvent>();
        using var watchStop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var watch = RecordAsync(app, states, watchStop.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceAsync("confera-migrations", e => e.Snapshot.ExitCode is not null and not 0, timeout.Token);
        // Aspire's dependency failure event proves gating after the failed worker.
        await app.ResourceNotifications.WaitForResourceAsync("confera-api", e => e.Snapshot.State?.Text == KnownResourceStates.FailedToStart, timeout.Token);
        Assert.DoesNotContain(states, e => e.Resource.Name == "confera-api" && e.Snapshot.State?.Text == KnownResourceStates.Running);
        watchStop.Cancel();
        await watch;
    }

    [Fact]
    public async Task TestOwnedDevelopmentVolumeSurvivesAppHostAndContainerRecreation()
    {
        using var timeout = Deadline();
        var volume = "confera-p1-" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var containerIds = new List<string>();
        Guid originalId;
        try
        {
            async Task<Guid> LaunchAsync(bool edit)
            {
                await using var builder = await CreateBuilderAsync(timeout.Token, password);
                builder.CreateResourceBuilder(Postgres(builder)).WithVolume(volume, "/var/lib/postgresql");
                var mount = Assert.Single(Postgres(builder).Annotations.OfType<ContainerMountAnnotation>());
                Assert.Equal(volume, mount.Source);
                Assert.Equal("/var/lib/postgresql", mount.Target);
                await using var app = await builder.BuildAsync(timeout.Token);
                await using var diagnostics = new AppHostDiagnostics(app);
                await app.StartAsync(timeout.Token);
                await app.ResourceNotifications.WaitForResourceHealthyAsync("confera-api", timeout.Token);
                var connection = await app.GetConnectionStringAsync("confera", timeout.Token);
                await using var db = new ConferaDbContext(PersistenceConfiguration.Options(connection!));
                Assert.Equal("/var/lib/postgresql/18/docker", await db.Database
                    .SqlQueryRaw<string>("SELECT current_setting('data_directory') AS \"Value\"").SingleAsync(timeout.Token));
                Assert.StartsWith("18.6", await db.Database
                    .SqlQueryRaw<string>("SELECT current_setting('server_version') AS \"Value\"").SingleAsync(timeout.Token));
                var containerId = (await DockerAsync(["ps", "-q", "--filter", $"volume={volume}"], timeout.Token)).Trim();
                Assert.NotEmpty(containerId);
                containerIds.Add(containerId);
                using var mounts = JsonDocument.Parse(await DockerAsync(["inspect", "--format", "{{json .Mounts}}", containerId], timeout.Token));
                var actualMount = Assert.Single(mounts.RootElement.EnumerateArray(), x => x.GetProperty("Name").GetString() == volume);
                Assert.Equal("/var/lib/postgresql", actualMount.GetProperty("Destination").GetString());
                Assert.DoesNotContain(mounts.RootElement.EnumerateArray(), x => x.GetProperty("Destination").GetString() == "/var/lib/postgresql/data");
                var room = await db.Rooms.SingleAsync(x => x.Name == (edit ? "Room A" : "Edited"), timeout.Token);
                if (edit)
                {
                    room.SetName("Edited");
                    room.SetHourlyRate(2500m);
                    await db.SaveChangesAsync(timeout.Token);
                }
                Assert.Equal(2500m, room.HourlyRate);
                Assert.Equal(3, await db.Rooms.CountAsync(timeout.Token));
                return room.Id;
            }
            originalId = await LaunchAsync(true);
            Assert.Empty((await DockerAsync(["ps", "-a", "-q", "--filter", $"id={containerIds[0]}"], timeout.Token)).Trim());
            Assert.Equal(originalId, await LaunchAsync(false));
            Assert.Equal(2, containerIds.Distinct().Count());
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await DockerAsync(["volume", "rm", volume], cleanup.Token);
        }
    }

    [Fact]
    public async Task ConcurrentAppHostsUseDifferentDatabasesPortsAndNoDevelopmentVolumes()
    {
        using var timeout = Deadline();
        await using var firstBuilder = await CreateBuilderAsync(timeout.Token);
        await using var secondBuilder = await CreateBuilderAsync(timeout.Token);
        Assert.Empty(Postgres(firstBuilder).Annotations.OfType<ContainerMountAnnotation>());
        Assert.Empty(Postgres(secondBuilder).Annotations.OfType<ContainerMountAnnotation>());
        await using var first = await firstBuilder.BuildAsync(timeout.Token);
        await using var second = await secondBuilder.BuildAsync(timeout.Token);
        await using var firstDiagnostics = new AppHostDiagnostics(first);
        await using var secondDiagnostics = new AppHostDiagnostics(second);
        await Task.WhenAll(first.StartAsync(timeout.Token), second.StartAsync(timeout.Token));
        await Task.WhenAll(first.ResourceNotifications.WaitForResourceHealthyAsync("confera-api", timeout.Token), second.ResourceNotifications.WaitForResourceHealthyAsync("confera-api", timeout.Token));
        var a = await first.GetConnectionStringAsync("confera", timeout.Token);
        var b = await second.GetConnectionStringAsync("confera", timeout.Token);
        Assert.NotEqual(new NpgsqlConnectionStringBuilder(a).Port, new NpgsqlConnectionStringBuilder(b).Port);
        await using var firstDb = new ConferaDbContext(PersistenceConfiguration.Options(a!));
        await using var secondDb = new ConferaDbContext(PersistenceConfiguration.Options(b!));
        var room = await firstDb.Rooms.FirstAsync(timeout.Token);
        room.SetName("Only first");
        await firstDb.SaveChangesAsync(timeout.Token);
        Assert.False(await secondDb.Rooms.AnyAsync(x => x.Name == "Only first", timeout.Token));
        using var aClient = first.CreateHttpClient("confera-api", "http");
        using var bClient = second.CreateHttpClient("confera-api", "http");
        Assert.NotEqual(aClient.BaseAddress, bClient.BaseAddress);
    }

    [Fact]
    public async Task SeedDisabledWorkerAppliesSchemaOnlyAndStopsSuccessfully()
    {
        using var timeout = Deadline();
        await using var builder = await CreateBuilderAsync(timeout.Token);
        builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(x => x.Name == "confera-migrations"))
            .WithEnvironment("DemoSeed__Enabled", "false");
        await using var app = await builder.BuildAsync(timeout.Token);
        await using var diagnostics = new AppHostDiagnostics(app);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("confera-api", timeout.Token);
        await app.ResourceNotifications.WaitForResourceAsync("confera-migrations", e => e.Snapshot.ExitCode == 0, timeout.Token);
        await using var db = new ConferaDbContext(PersistenceConfiguration.Options((await app.GetConnectionStringAsync("confera", timeout.Token))!));
        Assert.Empty(await db.Rooms.ToListAsync(timeout.Token));
        Assert.Empty(await db.PricingRules.ToListAsync(timeout.Token));
        Assert.Empty(await db.InitializationMarkers.ToListAsync(timeout.Token));
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync(timeout.Token));
    }

    private static CancellationTokenSource Deadline()
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        source.CancelAfter(TimeSpan.FromSeconds(170));
        return source;
    }

    private static async Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(CancellationToken ct, string? password = null)
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Confera_AppHost>(
            ["--Persistence:Isolated=true", $"--Parameters:confera-postgres-password={password ?? Guid.NewGuid().ToString("N")}"], ct);
        // Aspire keys auxiliary CLI sockets by AppHost:FilePath. Each in-process
        // test host needs its own identity so a local CLI run cannot stop it,
        // and two test hosts in the same process cannot overwrite one socket.
        builder.Configuration["AppHost:FilePath"] = Path.Combine(Path.GetTempPath(), $"confera-p1-{Guid.NewGuid():N}.csproj");
        // Resource states are archived explicitly; avoid forwarding arbitrary child
        // logs (which can contain local connection details) into CI artifacts.
        builder.Services.AddLogging(logging => logging.ClearProviders());
        return builder;
    }

    private static PostgresServerResource Postgres(IDistributedApplicationTestingBuilder builder) =>
        builder.Resources.OfType<PostgresServerResource>().Single();

    private static async Task RecordAsync(DistributedApplication app, ConcurrentQueue<ResourceEvent> states, CancellationToken ct)
    {
        try
        {
            await foreach (var state in app.ResourceNotifications.WatchAsync(ct)) states.Enqueue(state);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    internal static async Task<string> DockerAsync(string[] arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        Assert.True(process.ExitCode == 0, (await output) + (await error));
        return await output;
    }
}
