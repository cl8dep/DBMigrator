using DbMigrator.Config;
using DbMigrator.Migration;
using FluentAssertions;

namespace DbMigrator.Tests.Migration;

public class MigratorArgBuilderTests
{
    private static MigrationConfig MakeConfig(
        string srcDb = "source",
        string srcHost = "src-host",
        int srcPort = 5432,
        string srcUser = "postgres",
        string tgtDb = "target",
        string tgtHost = "tgt-host",
        int tgtPort = 5432,
        string tgtUser = "postgres",
        bool schemaOnly = false,
        string[]? excludeTables = null,
        string[]? extraArgs = null,
        int parallelJobs = 1) =>
        new()
        {
            Source = new DbConfig { Host = srcHost, Port = srcPort, Database = srcDb, User = srcUser },
            Target = new DbConfig { Host = tgtHost, Port = tgtPort, Database = tgtDb, User = tgtUser },
            Dump = new DumpConfig
            {
                SchemaOnly = schemaOnly,
                ExcludeTables = [.. (excludeTables ?? [])],
                ExtraPgDumpArgs = [.. (extraArgs ?? [])],
                ParallelJobs = parallelJobs
            }
        };

    // ── BuildPgDumpArgs ──────────────────────────────────────────────

    [Fact]
    public void BuildPgDumpArgs_ContainsAllRequiredFlags()
    {
        var config = MakeConfig(srcDb: "mydb", srcHost: "dbhost", srcPort: 5433, srcUser: "admin");
        var args = Migrator.BuildPgDumpArgs(config, "/tmp/dump.dump");

        args.Should().Contain("--host").And.Contain("dbhost");
        args.Should().Contain("--port").And.Contain("5433");
        args.Should().Contain("--username").And.Contain("admin");
        args.Should().Contain("--format").And.Contain("custom");
        args.Should().Contain("--verbose");
        args.Should().Contain("--no-password");
        args.Should().Contain("--file").And.Contain("/tmp/dump.dump");
        args.Should().Contain("mydb"); // database name always last
    }

    [Fact]
    public void BuildPgDumpArgs_DatabaseNameIsLastArg()
    {
        var config = MakeConfig(srcDb: "mydb");
        var args = Migrator.BuildPgDumpArgs(config, "/tmp/dump.dump");
        args.Last().Should().Be("mydb");
    }

    [Fact]
    public void BuildPgDumpArgs_SchemaOnlyAddsFlag()
    {
        var config = MakeConfig(schemaOnly: true);
        var args = Migrator.BuildPgDumpArgs(config, "/tmp/dump.dump");
        args.Should().Contain("--schema-only");
    }

    [Fact]
    public void BuildPgDumpArgs_SchemaOnlyFalseDoesNotAddFlag()
    {
        var config = MakeConfig(schemaOnly: false);
        var args = Migrator.BuildPgDumpArgs(config, "/tmp/dump.dump");
        args.Should().NotContain("--schema-only");
    }

    [Fact]
    public void BuildPgDumpArgs_ExcludesTablesAddsMultiplePairs()
    {
        var config = MakeConfig(excludeTables: ["logs", "sessions", "audit"]);
        var args = Migrator.BuildPgDumpArgs(config, "/tmp/dump.dump");

        // Each table requires "--exclude-table" + table name pair
        args.Where(a => a == "--exclude-table").Should().HaveCount(3);
        args.Should().Contain("logs").And.Contain("sessions").And.Contain("audit");
    }

    [Fact]
    public void BuildPgDumpArgs_ExtraArgsAppendedBeforeDatabase()
    {
        var config = MakeConfig(srcDb: "mydb", extraArgs: ["--lock-wait-timeout=30s"]);
        var args = Migrator.BuildPgDumpArgs(config, "/tmp/dump.dump");

        var extraIndex = args.IndexOf("--lock-wait-timeout=30s");
        var dbIndex = args.IndexOf("mydb");

        extraIndex.Should().BeGreaterThan(0, "extra args should be present");
        extraIndex.Should().BeLessThan(dbIndex, "extra args should come before the database name");
    }

    [Fact]
    public void BuildPgDumpArgs_NoExcludeTablesOrSchemaOnly_MinimalArgs()
    {
        var config = MakeConfig();
        var args = Migrator.BuildPgDumpArgs(config, "/tmp/dump.dump");

        args.Should().NotContain("--schema-only");
        args.Should().NotContain("--exclude-table");
    }

    [Fact]
    public void BuildPgDumpArgs_ParallelJobs_UsesDirectoryFormat()
    {
        var config = MakeConfig(parallelJobs: 4);
        var args = Migrator.BuildPgDumpArgs(config, "/tmp/dump-dir");

        var formatIndex = args.IndexOf("--format");
        args[formatIndex + 1].Should().Be("directory");
    }

    [Fact]
    public void BuildPgDumpArgs_ParallelJobs_AddsJobsFlag()
    {
        var config = MakeConfig(parallelJobs: 4);
        var args = Migrator.BuildPgDumpArgs(config, "/tmp/dump-dir");

        var jobsIndex = args.IndexOf("--jobs");
        jobsIndex.Should().BeGreaterThan(0);
        args[jobsIndex + 1].Should().Be("4");
    }

    [Fact]
    public void BuildPgDumpArgs_SingleJob_UsesCustomFormat_NoJobsFlag()
    {
        var config = MakeConfig(parallelJobs: 1);
        var args = Migrator.BuildPgDumpArgs(config, "/tmp/dump.dump");

        var formatIndex = args.IndexOf("--format");
        args[formatIndex + 1].Should().Be("custom");
        args.Should().NotContain("--jobs");
    }

    // ── BuildPgRestoreArgs ───────────────────────────────────────────

    [Fact]
    public void BuildPgRestoreArgs_ContainsAllRequiredFlags()
    {
        var config = MakeConfig(tgtDb: "target_db", tgtHost: "tgt-host", tgtPort: 5434, tgtUser: "admin");
        var args = Migrator.BuildPgRestoreArgs(config, "/tmp/dump.dump");

        args.Should().Contain("--host").And.Contain("tgt-host");
        args.Should().Contain("--port").And.Contain("5434");
        args.Should().Contain("--username").And.Contain("admin");
        args.Should().Contain("--dbname").And.Contain("target_db");
        args.Should().Contain("--no-password");
        args.Should().Contain("--clean");
        args.Should().Contain("--if-exists");
        args.Should().Contain("--verbose");
        args.Should().Contain("/tmp/dump.dump");
    }

    [Fact]
    public void BuildPgRestoreArgs_DumpPathIsLastArg()
    {
        var dumpPath = "/tmp/my-dump.dump";
        var config = MakeConfig();
        var args = Migrator.BuildPgRestoreArgs(config, dumpPath);
        args.Last().Should().Be(dumpPath);
    }

    [Fact]
    public void BuildPgRestoreArgs_AlwaysIncludesCleanAndIfExists()
    {
        var config = MakeConfig();
        var args = Migrator.BuildPgRestoreArgs(config, "/tmp/dump.dump");

        // --clean + --if-exists together ensures restore works on non-empty target
        args.Should().Contain("--clean");
        args.Should().Contain("--if-exists");
    }

    [Fact]
    public void BuildPgRestoreArgs_ParallelJobs_AddsJobsFlag()
    {
        var config = MakeConfig(parallelJobs: 4);
        var args = Migrator.BuildPgRestoreArgs(config, "/tmp/dump-dir");

        var jobsIndex = args.IndexOf("--jobs");
        jobsIndex.Should().BeGreaterThan(0);
        args[jobsIndex + 1].Should().Be("4");
    }

    [Fact]
    public void BuildPgRestoreArgs_ParallelJobs_DumpPathStillLast()
    {
        var dumpPath = "/tmp/dump-dir";
        var config = MakeConfig(parallelJobs: 4);
        var args = Migrator.BuildPgRestoreArgs(config, dumpPath);
        args.Last().Should().Be(dumpPath, "dump path must always be the final argument");
    }

    [Fact]
    public void BuildPgRestoreArgs_SingleJob_NoJobsFlag()
    {
        var config = MakeConfig(parallelJobs: 1);
        var args = Migrator.BuildPgRestoreArgs(config, "/tmp/dump.dump");
        args.Should().NotContain("--jobs");
    }
}
