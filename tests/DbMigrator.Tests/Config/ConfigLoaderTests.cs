using DbMigrator.Config;
using FluentAssertions;

namespace DbMigrator.Tests.Config;

public class ConfigLoaderTests
{
    [Fact]
    public void InterpolateEnvVars_ReplacesKnownVars()
    {
        Environment.SetEnvironmentVariable("TEST_PG_PASS", "supersecret");
        var input = "password: ${TEST_PG_PASS}";

        var result = ConfigLoader.InterpolateEnvVars(input);

        result.Should().Be("password: supersecret");
    }

    [Fact]
    public void InterpolateEnvVars_ThrowsWhenVarNotSet()
    {
        Environment.SetEnvironmentVariable("UNDEFINED_VAR_XYZ", null);
        var input = "password: ${UNDEFINED_VAR_XYZ}";

        var act = () => ConfigLoader.InterpolateEnvVars(input);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UNDEFINED_VAR_XYZ*");
    }

    [Fact]
    public void InterpolateEnvVars_LeavesNonVarStringsUntouched()
    {
        var input = "host: localhost\nport: 5432";
        var result = ConfigLoader.InterpolateEnvVars(input);
        result.Should().Be(input);
    }

    [Fact]
    public void InterpolateEnvVars_IgnoresEnvVarSyntaxInsideYamlComments()
    {
        // A comment like "# All values support ${ENV_VAR} interpolation." should NOT
        // cause a failure even if ENV_VAR is not set — comments are skipped.
        var input = "# All values support ${TOTALLY_UNSET_VAR} interpolation.\nhost: localhost";

        var act = () => ConfigLoader.InterpolateEnvVars(input);

        act.Should().NotThrow("${...} inside YAML comments must be ignored");
    }

    [Fact]
    public void InterpolateEnvVars_StillInterpolatesVarsOnNonCommentLines()
    {
        Environment.SetEnvironmentVariable("TEST_HOST", "db.example.com");
        var input = "# host is ${TEST_HOST} — ignored\nhost: ${TEST_HOST}";

        var result = ConfigLoader.InterpolateEnvVars(input);

        result.Should().Contain("host: db.example.com");
        result.Should().Contain("# host is ${TEST_HOST}"); // comment unchanged
    }

    [Fact]
    public void Load_ParsesValidYaml()
    {
        var yaml = """
            migration:
              source:
                host: source-host
                port: 5432
                database: mydb
                user: postgres
                password: pass
              target:
                host: target-host
                port: 5432
                database: mydb_copy
                user: postgres
                password: pass
              dump:
                schema_only: false
                exclude_tables: []
            sanitize:
              - table: users
                columns:
                  - name: email
                    strategy: static
                    value: redacted@example.com
            """;

        var path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);

        var config = ConfigLoader.Load(path);

        config.Migration.Source.Host.Should().Be("source-host");
        config.Migration.Source.Database.Should().Be("mydb");
        config.Migration.Target.Database.Should().Be("mydb_copy");
        config.Sanitize.Should().HaveCount(1);
        config.Sanitize[0].Table.Should().Be("users");
        config.Sanitize[0].Columns[0].Name.Should().Be("email");
        config.Sanitize[0].Columns[0].Strategy.Should().Be("static");

        File.Delete(path);
    }

    [Fact]
    public void Load_ThrowsWhenFileNotFound()
    {
        var act = () => ConfigLoader.Load("/nonexistent/path/config.yaml");
        act.Should().Throw<FileNotFoundException>();
    }

    [Theory]
    // faker with no faker field → error
    [InlineData("faker", null, "faker")]
    // template with no value → error
    [InlineData("template", null, "value")]
    public void ValidateStructure_DetectsInvalidStrategyConfig(
        string strategy, string? value, string missingField)
    {
        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = new DbConfig { Host = "h", Database = "db" },
                Target = new DbConfig { Host = "h", Database = "db2" }
            },
            Sanitize =
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns =
                    [
                        new ColumnRule { Name = "col", Strategy = strategy, Value = value }
                    ]
                }
            ]
        };

        var errors = ConfigLoader.ValidateStructure(config);

        errors.Should().NotBeEmpty(
            $"strategy '{strategy}' without '{missingField}' should produce an error");
    }

    [Fact]
    public void ValidateStructure_AllowsStaticWithNullValue()
    {
        // static + null value is valid at config level — the user explicitly wants to set NULL.
        // Whether the column accepts NULL is checked by SchemaValidator against the DB.
        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = new DbConfig { Host = "h", Database = "db" },
                Target = new DbConfig { Host = "h", Database = "db2" }
            },
            Sanitize =
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns = [new ColumnRule { Name = "col", Strategy = "static", Value = null }]
                }
            ]
        };

        var errors = ConfigLoader.ValidateStructure(config);
        errors.Should().BeEmpty("static with null value is intentional (sets column to NULL)");
    }

    [Fact]
    public void ValidateStructure_ReturnsErrorForUnknownStrategy()
    {
        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = new DbConfig { Host = "h", Database = "db" },
                Target = new DbConfig { Host = "h", Database = "db2" }
            },
            Sanitize =
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns = [new ColumnRule { Name = "col", Strategy = "unknown_strategy" }]
                }
            ]
        };

        var errors = ConfigLoader.ValidateStructure(config);
        errors.Should().Contain(e => e.Contains("unknown_strategy"));
    }

    [Fact]
    public void ValidateStructure_ReturnsErrorWhenSourceDatabaseMissing()
    {
        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = new DbConfig { Host = "h", Database = "" },
                Target = new DbConfig { Host = "h", Database = "db2" }
            }
        };

        var errors = ConfigLoader.ValidateStructure(config);
        errors.Should().Contain(e => e.Contains("source.database"));
    }

    [Fact]
    public void DbConfig_ToConnectionString_IncludesAllParts()
    {
        var config = new DbConfig
        {
            Host = "myhost",
            Port = 5433,
            Database = "mydb",
            User = "admin",
            Password = "secret"
        };

        var cs = config.ToConnectionString();

        cs.Should().Contain("Host=myhost");
        cs.Should().Contain("Port=5433");
        cs.Should().Contain("Database=mydb");
        cs.Should().Contain("Username=admin");
        cs.Should().Contain("Password=secret");
    }

    [Fact]
    public void ValidateStructure_ReturnsErrorWhenSourceAndTargetAreSameDb()
    {
        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = new DbConfig { Host = "same-host", Port = 5432, Database = "mydb" },
                Target = new DbConfig { Host = "same-host", Port = 5432, Database = "mydb" }
            }
        };

        var errors = ConfigLoader.ValidateStructure(config);
        errors.Should().Contain(e => e.Contains("same database"));
    }

    [Fact]
    public void ValidateStructure_AllowsDifferentDbOnSameHost()
    {
        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = new DbConfig { Host = "same-host", Port = 5432, Database = "mydb" },
                Target = new DbConfig { Host = "same-host", Port = 5432, Database = "mydb_copy" }
            }
        };

        var errors = ConfigLoader.ValidateStructure(config);
        errors.Should().NotContain(e => e.Contains("same database"));
    }

    [Theory]
    [InlineData("my table")]        // space
    [InlineData("my-table")]        // hyphen
    [InlineData("my.table")]        // dot
    [InlineData("my\"table")]       // double quote (injection risk)
    [InlineData("'; DROP TABLE--")] // SQL injection attempt
    public void ValidateStructure_RejectsUnsafeTableNames(string tableName)
    {
        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = new DbConfig { Host = "h", Database = "db" },
                Target = new DbConfig { Host = "h", Database = "db2" }
            },
            Sanitize =
            [
                new SanitizeRule
                {
                    Table = tableName,
                    Columns = [new ColumnRule { Name = "col", Strategy = "static", Value = "x" }]
                }
            ]
        };

        var errors = ConfigLoader.ValidateStructure(config);
        errors.Should().Contain(e => e.Contains("invalid characters") || e.Contains("empty"));
    }

    [Theory]
    [InlineData("col name")]
    [InlineData("col-name")]
    [InlineData("col\"name")]
    public void ValidateStructure_RejectsUnsafeColumnNames(string colName)
    {
        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = new DbConfig { Host = "h", Database = "db" },
                Target = new DbConfig { Host = "h", Database = "db2" }
            },
            Sanitize =
            [
                new SanitizeRule
                {
                    Table = "users",
                    Columns = [new ColumnRule { Name = colName, Strategy = "static", Value = "x" }]
                }
            ]
        };

        var errors = ConfigLoader.ValidateStructure(config);
        errors.Should().Contain(e => e.Contains("invalid characters"));
    }

    [Fact]
    public void ValidateStructure_ReturnsErrorForInvalidPort()
    {
        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = new DbConfig { Host = "h", Database = "db", Port = 0 },
                Target = new DbConfig { Host = "h", Database = "db2" }
            }
        };

        var errors = ConfigLoader.ValidateStructure(config);
        errors.Should().Contain(e => e.Contains("port"));
    }

    [Fact]
    public void ValidateStructure_ReturnsErrorForInvalidSslMode()
    {
        var config = new AppConfig
        {
            Migration = new MigrationConfig
            {
                Source = new DbConfig { Host = "h", Database = "db", SslMode = "Invalid" },
                Target = new DbConfig { Host = "h", Database = "db2" }
            }
        };

        var errors = ConfigLoader.ValidateStructure(config);
        errors.Should().Contain(e => e.Contains("ssl_mode"));
    }

    [Fact]
    public void ValidateStructure_AcceptsValidSslModes()
    {
        foreach (var mode in new[] { "Disable", "Prefer", "Require", "VerifyFull" })
        {
            var config = new AppConfig
            {
                Migration = new MigrationConfig
                {
                    Source = new DbConfig { Host = "h", Database = "db", SslMode = mode },
                    Target = new DbConfig { Host = "h", Database = "db2", SslMode = mode }
                }
            };

            var errors = ConfigLoader.ValidateStructure(config);
            errors.Should().NotContain(e => e.Contains("ssl_mode"),
                $"ssl_mode '{mode}' should be valid");
        }
    }
}
