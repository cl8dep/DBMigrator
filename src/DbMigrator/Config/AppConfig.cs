using YamlDotNet.Serialization;

namespace DbMigrator.Config;

public class AppConfig
{
    [YamlMember(Alias = "migration")]
    public MigrationConfig Migration { get; set; } = new();

    [YamlMember(Alias = "sanitize")]
    public List<SanitizeRule> Sanitize { get; set; } = [];

    /// <summary>
    /// Optional seed for the faker strategy. When set, every run produces the same
    /// fake values in the same order — useful for reproducible QA environments.
    /// Omit (or set to null) for random output on each run.
    /// </summary>
    [YamlMember(Alias = "faker_seed")]
    public int? FakerSeed { get; set; }
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

    /// <summary>
    /// Number of parallel workers for pg_dump / pg_restore (-j N).
    /// When > 1, format is automatically switched to "directory".
    /// Default: 1 (single-threaded).
    /// </summary>
    [YamlMember(Alias = "parallel_jobs")]
    public int ParallelJobs { get; set; } = 1;

    /// <summary>
    /// Persistent directory for the dump. When set, the dump is kept after
    /// the migration completes and reused on the next run if still valid
    /// (i.e. toc.dat is present). Useful in Cloud Run Jobs backed by a GCS
    /// volume mount so a failed restore does not require a full re-dump.
    /// If null, a temporary directory under Path.GetTempPath() is used and
    /// deleted after each run.
    /// </summary>
    [YamlMember(Alias = "dump_dir")]
    public string? DumpDir { get; set; }

    /// <summary>
    /// Maximum time allowed for a single pg_dump or pg_restore process, in minutes.
    /// Default: 120 (2 hours). Increase for very large databases.
    /// </summary>
    [YamlMember(Alias = "timeout_minutes")]
    public int? TimeoutMinutes { get; set; }
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

    /// <summary>Number of leading characters to keep visible (partial_mask strategy).</summary>
    [YamlMember(Alias = "keep_first")]
    public int? KeepFirst { get; set; }

    /// <summary>Number of trailing characters to keep visible (partial_mask strategy).</summary>
    [YamlMember(Alias = "keep_last")]
    public int? KeepLast { get; set; }

    /// <summary>Character used to replace masked characters. Default: '*'.</summary>
    [YamlMember(Alias = "mask_char")]
    public string? MaskChar { get; set; }

    /// <summary>List of values to pick from randomly (random_from strategy).</summary>
    [YamlMember(Alias = "values")]
    public List<string>? Values { get; set; }
}
