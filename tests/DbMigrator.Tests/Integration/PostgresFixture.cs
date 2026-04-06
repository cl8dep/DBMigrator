using Npgsql;
using Testcontainers.PostgreSql;

namespace DbMigrator.Tests.Integration;

/// <summary>
/// Shared Postgres container for all integration tests in the collection.
/// One container is started per test run, shared across all test classes
/// that use [Collection(PostgresFixture.CollectionName)].
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    public const string CollectionName = "Postgres";

    // PostgreSqlContainer has a built-in wait strategy (pg_isready) — no need to configure one
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("testdb")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Runs SQL against the shared container database.</summary>
    public async Task ExecuteSqlAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Creates a fresh database for a test to use in isolation.</summary>
    public async Task<string> CreateIsolatedDatabaseAsync(string dbName)
    {
        await ExecuteSqlAsync($"DROP DATABASE IF EXISTS \"{dbName}\"; CREATE DATABASE \"{dbName}\"");
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = dbName };
        return builder.ConnectionString;
    }
}

[CollectionDefinition(PostgresFixture.CollectionName)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
