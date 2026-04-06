using DbMigrator.Config;
using DbMigrator.Validation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DbMigrator.Tests.Integration;

[Collection(PostgresFixture.CollectionName)]
public class SchemaValidatorIntegrationTests(PostgresFixture postgres)
{
    private readonly SchemaValidator _validator = new(NullLogger<SchemaValidator>.Instance);

    // ── Helpers ─────────────────────────────────────────────────────

    private AppConfig MakeConfig(string connString, List<SanitizeRule> rules) => new()
    {
        Migration = new MigrationConfig
        {
            Source = ParseDbConfig(connString),
            Target = ParseDbConfig(connString),
        },
        Sanitize = rules
    };

    private static DbConfig ParseDbConfig(string connString, string schema = "public")
    {
        var b = new Npgsql.NpgsqlConnectionStringBuilder(connString);
        return new DbConfig
        {
            Host = b.Host ?? "localhost",
            Port = b.Port,
            Database = b.Database ?? "testdb",
            User = b.Username ?? "test",
            Password = b.Password ?? "test",
            SslMode = "Disable",
            Schema = schema
        };
    }

    private async Task<string> SetupSchemaAsync(string tableSql)
    {
        // Each test gets an isolated DB to avoid state leakage
        var dbName = $"test_{Guid.NewGuid():N}"[..20];
        var connString = await postgres.CreateIsolatedDatabaseAsync(dbName);

        await using var conn = new Npgsql.NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = tableSql;
        await cmd.ExecuteNonQueryAsync();

        return connString;
    }

    // ── Table existence ──────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_TableDoesNotExist_ReturnsError()
    {
        var connString = await SetupSchemaAsync("CREATE TABLE existing_table (id SERIAL PRIMARY KEY)");
        var config = MakeConfig(connString, [
            new SanitizeRule
            {
                Table = "nonexistent_table",
                Columns = [new ColumnRule { Name = "col", Strategy = "static", Value = "x" }]
            }
        ]);

        var result = await _validator.ValidateAsync(config);

        result.HasErrors.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("nonexistent_table"));
    }

    [Fact]
    public async Task ValidateAsync_TableExists_NoError()
    {
        var connString = await SetupSchemaAsync(
            "CREATE TABLE users (id SERIAL PRIMARY KEY, email TEXT NOT NULL)");
        var config = MakeConfig(connString, [
            new SanitizeRule
            {
                Table = "users",
                Columns = [new ColumnRule { Name = "email", Strategy = "static", Value = "x@x.com" }]
            }
        ]);

        var result = await _validator.ValidateAsync(config);

        result.HasErrors.Should().BeFalse();
    }

    // ── Column existence ─────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_ColumnDoesNotExist_ReturnsError()
    {
        var connString = await SetupSchemaAsync(
            "CREATE TABLE users (id SERIAL PRIMARY KEY, email TEXT)");
        var config = MakeConfig(connString, [
            new SanitizeRule
            {
                Table = "users",
                Columns = [new ColumnRule { Name = "nonexistent_column", Strategy = "static", Value = "x" }]
            }
        ]);

        var result = await _validator.ValidateAsync(config);

        result.HasErrors.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("nonexistent_column"));
    }

    // ── NOT NULL checks ──────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_NullValueOnNotNullColumn_ReturnsError()
    {
        var connString = await SetupSchemaAsync(
            "CREATE TABLE users (id SERIAL PRIMARY KEY, email TEXT NOT NULL)");
        var config = MakeConfig(connString, [
            new SanitizeRule
            {
                Table = "users",
                Columns = [new ColumnRule { Name = "email", Strategy = "null_value" }]
            }
        ]);

        var result = await _validator.ValidateAsync(config);

        result.HasErrors.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("NOT NULL") && e.Message.Contains("email"));
    }

    [Fact]
    public async Task ValidateAsync_NullValueOnNullableColumn_NoError()
    {
        var connString = await SetupSchemaAsync(
            "CREATE TABLE users (id SERIAL PRIMARY KEY, avatar_url TEXT)");
        var config = MakeConfig(connString, [
            new SanitizeRule
            {
                Table = "users",
                Columns = [new ColumnRule { Name = "avatar_url", Strategy = "null_value" }]
            }
        ]);

        var result = await _validator.ValidateAsync(config);

        result.HasErrors.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_StaticNullOnNotNullColumn_ReturnsError()
    {
        var connString = await SetupSchemaAsync(
            "CREATE TABLE users (id SERIAL PRIMARY KEY, name TEXT NOT NULL)");
        var config = MakeConfig(connString, [
            new SanitizeRule
            {
                Table = "users",
                Columns = [new ColumnRule { Name = "name", Strategy = "static", Value = null }]
            }
        ]);

        var result = await _validator.ValidateAsync(config);

        result.HasErrors.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("NOT NULL") && e.Message.Contains("name"));
    }

    // ── Primary key warning ──────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_ModifyingPrimaryKey_ReturnsWarning()
    {
        var connString = await SetupSchemaAsync(
            "CREATE TABLE users (id SERIAL PRIMARY KEY, email TEXT)");
        var config = MakeConfig(connString, [
            new SanitizeRule
            {
                Table = "users",
                Columns = [new ColumnRule { Name = "id", Strategy = "static", Value = "999" }]
            }
        ]);

        var result = await _validator.ValidateAsync(config);

        result.HasErrors.Should().BeFalse("modifying a PK is a warning, not a hard error");
        result.Warnings.Should().Contain(w => w.Message.Contains("PRIMARY KEY") && w.Message.Contains("id"));
    }

    // ── Empty table warning ──────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_EmptyTable_ReturnsWarning()
    {
        var connString = await SetupSchemaAsync(
            "CREATE TABLE users (id SERIAL PRIMARY KEY, email TEXT)");
        var config = MakeConfig(connString, [
            new SanitizeRule
            {
                Table = "users",
                Columns = [new ColumnRule { Name = "email", Strategy = "static", Value = "x" }]
            }
        ]);

        var result = await _validator.ValidateAsync(config);

        result.Warnings.Should().Contain(w => w.Message.Contains("0 rows"));
    }

    [Fact]
    public async Task ValidateAsync_NonEmptyTable_NoRowCountWarning()
    {
        var connString = await SetupSchemaAsync(
            "CREATE TABLE users (id SERIAL PRIMARY KEY, email TEXT); " +
            "INSERT INTO users (email) VALUES ('a@a.com')");
        var config = MakeConfig(connString, [
            new SanitizeRule
            {
                Table = "users",
                Columns = [new ColumnRule { Name = "email", Strategy = "static", Value = "x" }]
            }
        ]);

        var result = await _validator.ValidateAsync(config);

        result.Warnings.Should().NotContain(w => w.Message.Contains("0 rows"));
    }

    // ── Faker type mismatch warning ───────────────────────────────────

    [Fact]
    public async Task ValidateAsync_FakerStringMethodOnNumericColumn_ReturnsWarning()
    {
        var connString = await SetupSchemaAsync(
            "CREATE TABLE orders (id SERIAL PRIMARY KEY, amount INTEGER)");
        var config = MakeConfig(connString, [
            new SanitizeRule
            {
                Table = "orders",
                Columns = [new ColumnRule { Name = "amount", Strategy = "faker", Faker = "first_name" }]
            }
        ]);

        var result = await _validator.ValidateAsync(config);

        result.Warnings.Should().Contain(w => w.Message.Contains("numeric") || w.Message.Contains("first_name"));
    }

    [Fact]
    public async Task ValidateAsync_FakerNumericMethodOnNumericColumn_NoWarning()
    {
        var connString = await SetupSchemaAsync(
            "CREATE TABLE orders (id SERIAL PRIMARY KEY, amount INTEGER)");
        var config = MakeConfig(connString, [
            new SanitizeRule
            {
                Table = "orders",
                Columns = [new ColumnRule { Name = "amount", Strategy = "faker", Faker = "random_number" }]
            }
        ]);

        var result = await _validator.ValidateAsync(config);

        result.Warnings.Should().NotContain(w => w.Message.Contains("numeric"));
    }

    // ── Connection failure ───────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_CannotConnect_ReturnsError()
    {
        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = new DbConfig
                {
                    Host = "nonexistent-host-xyz",
                    Port = 5432,
                    Database = "db",
                    User = "u",
                    Password = "p",
                    SslMode = "Disable"
                },
                Target = new DbConfig { Host = "h", Database = "d" }
            },
            Sanitize =
            [
                new SanitizeRule
                {
                    Table = "t",
                    Columns = [new ColumnRule { Name = "c", Strategy = "static", Value = "x" }]
                }
            ]
        };

        var result = await _validator.ValidateAsync(config);

        result.HasErrors.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("Cannot connect"));
    }

    // ── No rules → skip validation ───────────────────────────────────

    [Fact]
    public async Task ValidateAsync_NoSanitizeRules_ReturnsEmptyResult()
    {
        var config = MakeConfig(postgres.ConnectionString, []);

        var result = await _validator.ValidateAsync(config);

        result.Issues.Should().BeEmpty();
        result.HasErrors.Should().BeFalse();
    }

    // ── Custom schema ────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_CustomSchema_FindsTableInCorrectSchema()
    {
        var connString = await SetupSchemaAsync(
            "CREATE SCHEMA warehouse; " +
            "CREATE TABLE warehouse.products (id SERIAL PRIMARY KEY, name TEXT NOT NULL)");

        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = ParseDbConfig(connString, schema: "warehouse"),
                Target = ParseDbConfig(connString)
            },
            Sanitize =
            [
                new SanitizeRule
                {
                    Table = "products",
                    Columns = [new ColumnRule { Name = "name", Strategy = "static", Value = "REDACTED" }]
                }
            ]
        };

        var result = await _validator.ValidateAsync(config);

        result.HasErrors.Should().BeFalse("table exists in the 'warehouse' schema");
    }

    [Fact]
    public async Task ValidateAsync_TableInPublicButSchemaSetToOther_ReturnsError()
    {
        var connString = await SetupSchemaAsync(
            "CREATE TABLE users (id SERIAL PRIMARY KEY, email TEXT)");

        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = ParseDbConfig(connString, schema: "other_schema"),
                Target = ParseDbConfig(connString)
            },
            Sanitize =
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns = [new ColumnRule { Name = "email", Strategy = "static", Value = "x" }]
                }
            ]
        };

        var result = await _validator.ValidateAsync(config);

        result.HasErrors.Should().BeTrue("table is in public, not other_schema");
    }
}
