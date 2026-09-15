using Confera.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Confera.Testing.PostgresFixture))]

namespace Confera.Testing;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18.6").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public async Task<TestDatabase> CreateDatabaseAsync(bool migrate = true)
    {
        var name = "p1_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_container.GetConnectionString());
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\" ENCODING 'UTF8' TEMPLATE template0", admin);
        await create.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name };
        var database = new TestDatabase(connection.ConnectionString, _container.GetConnectionString(), name);
        try
        {
            await using var db = database.Context();
            if (migrate) await db.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }
}

public sealed class TestDatabase(string connectionString, string adminConnectionString, string name) : IAsyncDisposable
{
    public string ConnectionString => connectionString;
    public string Name => name;
    public ConferaDbContext Context(bool retries = false) => new(PersistenceConfiguration.Options(connectionString, retries));

    public async Task<int> SqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        using var pool = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(pool);
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
