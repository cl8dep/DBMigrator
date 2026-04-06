using DbMigrator.Config;
using DbMigrator.Sanitization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace DbMigrator.Tests.Integration;

[Collection(PostgresFixture.CollectionName)]
public class SanitizerIntegrationTests(PostgresFixture postgres)
{
    private readonly Sanitizer _sanitizer = new(NullLogger<Sanitizer>.Instance);

    // ── Helpers ─────────────────────────────────────────────────────

    private async Task<(string ConnString, AppConfig Config)> SetupAsync(
        string schemaSql,
        List<SanitizeRule> rules)
    {
        var dbName = $"san_{Guid.NewGuid():N}"[..20];
        var connString = await postgres.CreateIsolatedDatabaseAsync(dbName);

        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = schemaSql;
        await cmd.ExecuteNonQueryAsync();

        var b = new NpgsqlConnectionStringBuilder(connString);
        var dbConfig = new DbConfig
        {
            Host = b.Host ?? "localhost",
            Port = b.Port,
            Database = b.Database ?? dbName,
            User = b.Username ?? "test",
            Password = b.Password ?? "test",
            SslMode = "Disable"
        };

        var config = new AppConfig
        {
            Migration = new MigrationConfig { Source = dbConfig, Target = dbConfig },
            Sanitize = rules
        };

        return (connString, config);
    }

    private static async Task<T?> QueryScalarAsync<T>(string connString, string sql)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? default : (T)Convert.ChangeType(result, typeof(T));
    }

    // ── Static strategy ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_StaticStrategy_UpdatesAllRows()
    {
        var (connString, config) = await SetupAsync(
            """
            CREATE TABLE users (id SERIAL PRIMARY KEY, email TEXT NOT NULL);
            INSERT INTO users (email) VALUES ('real@company.com'), ('also.real@company.com');
            """,
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns = [new ColumnRule { Name = "email", Strategy = "static", Value = "redacted@test.local" }]
                }
            ]);

        var report = await _sanitizer.RunAsync(config);

        report.TotalRowsAffected.Should().Be(2);

        var count = await QueryScalarAsync<long>(connString,
            "SELECT COUNT(*) FROM users WHERE email = 'redacted@test.local'");
        count.Should().Be(2, "all rows should have the static value");

        var originalCount = await QueryScalarAsync<long>(connString,
            "SELECT COUNT(*) FROM users WHERE email LIKE '%company.com'");
        originalCount.Should().Be(0, "no original emails should remain");
    }

    // ── Null value strategy ───────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NullValueStrategy_SetsColumnsToNull()
    {
        var (connString, config) = await SetupAsync(
            """
            CREATE TABLE users (id SERIAL PRIMARY KEY, avatar_url TEXT);
            INSERT INTO users (avatar_url) VALUES ('https://cdn.example.com/1.jpg');
            """,
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns = [new ColumnRule { Name = "avatar_url", Strategy = "null_value" }]
                }
            ]);

        await _sanitizer.RunAsync(config);

        var nullCount = await QueryScalarAsync<long>(connString,
            "SELECT COUNT(*) FROM users WHERE avatar_url IS NULL");
        nullCount.Should().Be(1);
    }

    // ── Hash strategy ─────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_HashStrategy_ReplacesWithSha256Hex()
    {
        var (connString, config) = await SetupAsync(
            """
            CREATE TABLE users (id SERIAL PRIMARY KEY, national_id TEXT);
            INSERT INTO users (national_id) VALUES ('123-45-6789');
            """,
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns = [new ColumnRule { Name = "national_id", Strategy = "hash" }]
                }
            ]);

        await _sanitizer.RunAsync(config);

        var hashed = await QueryScalarAsync<string>(connString,
            "SELECT national_id FROM users LIMIT 1");

        hashed.Should().NotBeNullOrEmpty();
        hashed.Should().MatchRegex("^[0-9a-f]{64}$", "SHA256 hex is 64 lowercase hex chars");
        hashed.Should().NotBe("123-45-6789", "original value should be gone");

        // Verify it matches what our C# HashStrategy.HashValue produces
        var expected = DbMigrator.Sanitization.Strategies.HashStrategy.HashValue("123-45-6789");
        hashed.Should().Be(expected, "hash must be deterministic and match our C# implementation");
    }

    // ── Template strategy ─────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_TemplateStrategy_InterpolatesRowValues()
    {
        var (connString, config) = await SetupAsync(
            """
            CREATE TABLE users (id SERIAL PRIMARY KEY, email TEXT);
            INSERT INTO users (email) VALUES ('original@company.com');
            """,
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns = [new ColumnRule { Name = "email", Strategy = "template", Value = "user_{id}@redacted.local" }]
                }
            ]);

        await _sanitizer.RunAsync(config);

        var email = await QueryScalarAsync<string>(connString, "SELECT email FROM users LIMIT 1");
        email.Should().MatchRegex(@"^user_\d+@redacted\.local$");
        email.Should().NotContain("original");
    }

    // ── Faker strategy ────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_FakerStrategy_ReplacesWithFakeData()
    {
        var (connString, config) = await SetupAsync(
            """
            CREATE TABLE users (id SERIAL PRIMARY KEY, first_name TEXT, last_name TEXT);
            INSERT INTO users (first_name, last_name) VALUES ('John', 'Doe'), ('Jane', 'Smith');
            """,
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns =
                    [
                        new ColumnRule { Name = "first_name", Strategy = "faker", Faker = "first_name" },
                        new ColumnRule { Name = "last_name", Strategy = "faker", Faker = "last_name" }
                    ]
                }
            ]);

        await _sanitizer.RunAsync(config);

        var johnCount = await QueryScalarAsync<long>(connString,
            "SELECT COUNT(*) FROM users WHERE first_name = 'John'");
        johnCount.Should().Be(0, "original first names should be gone");

        var doeCount = await QueryScalarAsync<long>(connString,
            "SELECT COUNT(*) FROM users WHERE last_name = 'Doe'");
        doeCount.Should().Be(0, "original last names should be gone");

        var totalCount = await QueryScalarAsync<long>(connString,
            "SELECT COUNT(*) FROM users WHERE first_name IS NOT NULL AND last_name IS NOT NULL");
        totalCount.Should().Be(2, "all rows should have been updated with fake values");
    }

    // ── WHERE clause ──────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_WithWhereClause_OnlyAffectsMatchingRows()
    {
        var (connString, config) = await SetupAsync(
            """
            CREATE TABLE users (id SERIAL PRIMARY KEY, role TEXT, email TEXT);
            INSERT INTO users (role, email) VALUES ('user', 'user@company.com'), ('admin', 'admin@company.com');
            """,
            [
                new SanitizeRule
                {
                    Table = "users",
                    Where = "role = 'user'",
                    Columns = [new ColumnRule { Name = "email", Strategy = "static", Value = "redacted@test.local" }]
                }
            ]);

        var report = await _sanitizer.RunAsync(config);

        report.TotalRowsAffected.Should().Be(1, "only the non-admin row should be updated");

        var adminEmail = await QueryScalarAsync<string>(connString,
            "SELECT email FROM users WHERE role = 'admin'");
        adminEmail.Should().Be("admin@company.com", "admin email should be untouched");

        var userEmail = await QueryScalarAsync<string>(connString,
            "SELECT email FROM users WHERE role = 'user'");
        userEmail.Should().Be("redacted@test.local");
    }

    // ── Multiple tables ───────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_MultipleRules_AppliesAllInOneTransaction()
    {
        var (connString, config) = await SetupAsync(
            """
            CREATE TABLE users (id SERIAL PRIMARY KEY, email TEXT);
            CREATE TABLE orders (id SERIAL PRIMARY KEY, billing_address TEXT);
            INSERT INTO users (email) VALUES ('real@company.com');
            INSERT INTO orders (billing_address) VALUES ('123 Real St');
            """,
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns = [new ColumnRule { Name = "email", Strategy = "static", Value = "x@x.com" }]
                },
                new SanitizeRule
                {
                    Table = "orders",
                    Columns = [new ColumnRule { Name = "billing_address", Strategy = "static", Value = "REDACTED" }]
                }
            ]);

        var report = await _sanitizer.RunAsync(config);

        report.Results.Should().HaveCount(2);
        report.TotalRowsAffected.Should().Be(2);

        var email = await QueryScalarAsync<string>(connString, "SELECT email FROM users");
        email.Should().Be("x@x.com");

        var addr = await QueryScalarAsync<string>(connString, "SELECT billing_address FROM orders");
        addr.Should().Be("REDACTED");
    }

    // ── Transaction: rollback on error ────────────────────────────────

    [Fact]
    public async Task RunAsync_ErrorMidway_RollsBackEntireTransaction()
    {
        var (connString, config) = await SetupAsync(
            """
            CREATE TABLE users (id SERIAL PRIMARY KEY, email TEXT NOT NULL);
            INSERT INTO users (email) VALUES ('real@company.com');
            """,
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns = [new ColumnRule { Name = "email", Strategy = "static", Value = "x@x.com" }]
                },
                // This rule will fail: column does not exist → exception → rollback
                new SanitizeRule
                {
                    Table = "users",
                    Columns = [new ColumnRule { Name = "nonexistent_col", Strategy = "static", Value = "x" }]
                }
            ]);

        var act = async () => await _sanitizer.RunAsync(config);
        await act.Should().ThrowAsync<Exception>("the second rule targets a non-existent column");

        // The first rule's UPDATE should have been rolled back
        var email = await QueryScalarAsync<string>(connString, "SELECT email FROM users");
        email.Should().Be("real@company.com", "transaction should have been rolled back");
    }

    // ── Dry run ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DryRun_DoesNotModifyData()
    {
        var (connString, config) = await SetupAsync(
            """
            CREATE TABLE users (id SERIAL PRIMARY KEY, email TEXT);
            INSERT INTO users (email) VALUES ('real@company.com');
            """,
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns = [new ColumnRule { Name = "email", Strategy = "static", Value = "x@x.com" }]
                }
            ]);

        var report = await _sanitizer.RunAsync(config, dryRun: true);

        report.TotalRowsAffected.Should().Be(0, "dry run should not execute any updates");

        var email = await QueryScalarAsync<string>(connString, "SELECT email FROM users");
        email.Should().Be("real@company.com", "data should be untouched in dry run");
    }

    // ── No rules → no-op ─────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NoSanitizeRules_ReturnsEmptyReport()
    {
        var (_, config) = await SetupAsync("CREATE TABLE t (id INT)", []);

        var report = await _sanitizer.RunAsync(config);

        report.Results.Should().BeEmpty();
        report.TotalRowsAffected.Should().Be(0);
    }
}
