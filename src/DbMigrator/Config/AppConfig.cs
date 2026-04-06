using YamlDotNet.Serialization;

namespace DbMigrator.Config;

public class AppConfig
{
    [YamlMember(Alias = "migration")]
    public MigrationConfig Migration { get; set; } = new();

    [YamlMember(Alias = "sanitize")]
    public List<SanitizeRule> Sanitize { get; set; } = [];
}

public class MigrationConfig
{
    [YamlMember(Alias = "source")]
    public DbConfig Source { get; set; } = new();

    [YamlMember(Alias = "target")]
    public DbConfig Target { get; set; } = new();

    [YamlMember(Alias = "dump")]
    public DumpConfig Dump { get; set; } = new();
}

public class DbConfig
{
    [YamlMember(Alias = "host")]
    public string Host { get; set; } = "localhost";

    [YamlMember(Alias = "port")]
    public int Port { get; set; } = 5432;

    [YamlMember(Alias = "database")]
    public string Database { get; set; } = string.Empty;

    [YamlMember(Alias = "user")]
    public string User { get; set; } = "postgres";

    [YamlMember(Alias = "password")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// PostgreSQL schema to use for information_schema queries and sanitization.
    /// Defaults to "public". Change if your tables live in a custom schema.
    /// </summary>
    [YamlMember(Alias = "schema")]
    public string Schema { get; set; } = "public";

    /// <summary>SSL mode: Disable, Allow, Prefer, Require, VerifyCA, VerifyFull</summary>
    [YamlMember(Alias = "ssl_mode")]
    public string SslMode { get; set; } = "Prefer";

    public string ToConnectionString() =>
        $"Host={Host};Port={Port};Database={Database};Username={User};Password={Password};" +
        $"SSL Mode={SslMode};Include Error Detail=true";
}

public class DumpConfig
{
    [YamlMember(Alias = "exclude_tables")]
    public List<string> ExcludeTables { get; set; } = [];

    [YamlMember(Alias = "schema_only")]
    public bool SchemaOnly { get; set; } = false;

    [YamlMember(Alias = "extra_pg_dump_args")]
    public List<string> ExtraPgDumpArgs { get; set; } = [];
}

public class SanitizeRule
{
    [YamlMember(Alias = "table")]
    public string Table { get; set; } = string.Empty;

    /// <summary>
    /// Optional raw WHERE clause appended to the UPDATE statement.
    /// This is trusted config — not exposed to end users. Use with care.
    /// </summary>
    [YamlMember(Alias = "where")]
    public string? Where { get; set; }

    [YamlMember(Alias = "columns")]
    public List<ColumnRule> Columns { get; set; } = [];
}

public class ColumnRule
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "strategy")]
    public string Strategy { get; set; } = string.Empty;

    [YamlMember(Alias = "value")]
    public string? Value { get; set; }

    [YamlMember(Alias = "faker")]
    public string? Faker { get; set; }
}
