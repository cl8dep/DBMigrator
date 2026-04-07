using System.Diagnostics;
using DbMigrator.Config;
using Microsoft.Extensions.Logging;
using Npgsql;
using Serilog;
using Spectre.Console;

namespace DbMigrator.Migration;

public class Migrator(ILogger<Migrator> logger)
{
    /// <summary>Maximum time allowed for each subprocess (pg_dump or pg_restore).</summary>
    public TimeSpan ProcessTimeout { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Pass --verbose to pg_dump / pg_restore and stream their stderr output live.</summary>
    public bool Verbose { get; set; }

    /// <summary>Print the full argument list for each subprocess before executing.</summary>
    public bool PrintArgs { get; set; }

    public async Task RunAsync(MigrationConfig config, string? dumpDirOverride = null, CancellationToken ct = default)
    {
        // Resolve dump directory: CLI flag > config > temp
        // When a persistent dump dir is used we always use directory format (required for toc.dat detection).
        var persistentDumpDir = dumpDirOverride ?? config.Dump.DumpDir;
        bool persistent = persistentDumpDir is not null;
        bool parallel = persistent || config.Dump.ParallelJobs > 1;

        var dumpPath = persistent
            ? persistentDumpDir!
            : parallel
                ? Path.Combine(Path.GetTempPath(), $"db-migrator-dump-{Guid.NewGuid():N}")
                : Path.Combine(Path.GetTempPath(), $"db-migrator-dump-{Guid.NewGuid():N}.sql");

        try
        {
            // ── Dump ────────────────────────────────────────────────────────
            if (persistent && IsValidDump(dumpPath))
            {
                var existingSize = new DirectoryInfo(dumpPath)
                    .EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                AnsiConsole.MarkupLine(
                    $"[yellow]⚡ Reusing existing dump[/] at [cyan]{dumpPath}[/] ({existingSize:N0} bytes) — skipping pg_dump");
                logger.LogInformation("Reusing existing dump at {Path} ({Size:N0} bytes) — skipping pg_dump",
                    dumpPath, existingSize);
            }
            else
            {
                if (persistent)
                {
                    // Previous dump in this dir was invalid/partial — clean it up before retrying
                    if (Directory.Exists(dumpPath))
                    {
                        AnsiConsole.MarkupLine($"[yellow]⚠ Stale dump found at {dumpPath} — removing before re-dump[/]");
                        Directory.Delete(dumpPath, recursive: true);
                    }
                    Directory.CreateDirectory(dumpPath);
                }

                logger.LogInformation("Starting dump from {Database}@{Host}:{Port}",
                    config.Source.Database, config.Source.Host, config.Source.Port);

                var sw = Stopwatch.StartNew();
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan"))
                    .StartAsync(
                        $"[cyan]pg_dump[/] {config.Source.Database}@{config.Source.Host}:{config.Source.Port} ...",
                        async ctx =>
                        {
                            await DumpAsync(config, dumpPath, parallel,
                                line => ctx.Status($"[cyan]pg_dump[/] {Markup.Escape(Truncate(line))}"),
                                ct);
                        });
                sw.Stop();

                long dumpSize = parallel
                    ? new DirectoryInfo(dumpPath).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                    : new FileInfo(dumpPath).Length;

                AnsiConsole.MarkupLine(
                    $"[green]✓ Dump completed[/] in {sw.Elapsed:g} ({dumpSize:N0} bytes)");
                logger.LogInformation("Dump completed in {Elapsed:g} ({Size:N0} bytes)", sw.Elapsed, dumpSize);

                if (persistent)
                    AnsiConsole.MarkupLine($"[grey]  Dump persisted at {dumpPath}[/]");
            }

            // ── Restore ─────────────────────────────────────────────────────
            logger.LogInformation("Starting restore to {Database}@{Host}:{Port}",
                config.Target.Database, config.Target.Host, config.Target.Port);

            var restoreSw = Stopwatch.StartNew();
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync(
                    $"[cyan]pg_restore[/] → {config.Target.Database}@{config.Target.Host}:{config.Target.Port} ...",
                    async ctx =>
                    {
                        await RestoreAsync(config, dumpPath, parallel,
                            line => ctx.Status($"[cyan]pg_restore[/] {Markup.Escape(Truncate(line))}"),
                            ct);
                    });
            restoreSw.Stop();

            AnsiConsole.MarkupLine($"[green]✓ Restore completed[/] in {restoreSw.Elapsed:g}");
            logger.LogInformation("Restore completed in {Elapsed:g}", restoreSw.Elapsed);
        }
        finally
        {
            // Only delete the dump if it's in a temp directory — persistent dumps survive for reuse
            if (!persistent)
                CleanupDump(dumpPath, parallel);
        }
    }

    /// <summary>A directory format dump is valid when toc.dat is present and non-empty.</summary>
    private static bool IsValidDump(string path) =>
        Directory.Exists(path) &&
        File.Exists(Path.Combine(path, "toc.dat")) &&
        new FileInfo(Path.Combine(path, "toc.dat")).Length > 0;

    private async Task DumpAsync(
        MigrationConfig config, string dumpPath, bool useDirectoryFormat, Action<string> onProgress, CancellationToken ct)
    {
        var args = BuildPgDumpArgs(config, dumpPath, useDirectoryFormat, Verbose);
        var env = new Dictionary<string, string> { ["PGPASSWORD"] = config.Source.Password };
        await RunProcessAsync("pg_dump", args, env, onProgress, ct);
    }

    private async Task RestoreAsync(
        MigrationConfig config, string dumpPath, bool useDirectoryFormat, Action<string> onProgress, CancellationToken ct)
    {
        var args = BuildPgRestoreArgs(config, dumpPath, useDirectoryFormat, Verbose);
        var env = new Dictionary<string, string> { ["PGPASSWORD"] = config.Target.Password };
        // pg_restore exits with code 1 when using --clean on a fresh database because all
        // DROP IF EXISTS statements "fail" (objects don't exist yet). The data IS restored.
        // Only exit code 3 is a true fatal failure in pg_restore.
        await RunProcessAsync("pg_restore", args, env, onProgress, ct, warningExitCode: 1);
    }

    public static List<string> BuildPgDumpArgs(MigrationConfig config, string dumpPath, bool directoryFormat, bool verbose = false)
    {
        bool parallel = config.Dump.ParallelJobs > 1;
        var args = new List<string>
        {
            "--host",     config.Source.Host,
            "--port",     config.Source.Port.ToString(),
            "--username", config.Source.User,
            "--format",   directoryFormat ? "directory" : "custom",
            "--no-password",
            "--file",     dumpPath
        };

        if (verbose)
            args.Add("--verbose");

        if (parallel)
        {
            args.Add("--jobs");
            args.Add(config.Dump.ParallelJobs.ToString());
        }

        if (config.Dump.SchemaOnly)
            args.Add("--schema-only");

        foreach (var table in config.Dump.ExcludeTables)
        {
            args.Add("--exclude-table");
            args.Add(table);
        }

        args.AddRange(config.Dump.ExtraPgDumpArgs);
        args.Add(config.Source.Database);

        return args;
    }

    public static List<string> BuildPgRestoreArgs(MigrationConfig config, string dumpPath, bool directoryFormat, bool verbose = false)
    {
        bool parallel = directoryFormat && config.Dump.ParallelJobs > 1;
        var args = new List<string>
        {
            "--host",     config.Target.Host,
            "--port",     config.Target.Port.ToString(),
            "--username", config.Target.User,
            "--dbname",   config.Target.Database,
            "--no-password",
            "--clean",
            "--if-exists"
        };

        if (verbose)
            args.Add("--verbose");

        if (parallel)
        {
            args.Add("--jobs");
            args.Add(config.Dump.ParallelJobs.ToString());
        }

        args.Add(dumpPath);
        return args;
    }

    public static async Task RunPreflightChecksAsync(MigrationConfig config, CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[grey]Running pre-flight checks...[/]");
        Log.Information("Running pre-flight checks");

        // ── 1. Connectivity: source ──────────────────────────────────────────
        await using var sourceConn = new NpgsqlConnection(config.Source.ToConnectionString());
        try
        {
            await sourceConn.OpenAsync(ct);
            AnsiConsole.MarkupLine(
                $"[green]✓ Source DB reachable:[/] {config.Source.Database}@{config.Source.Host}:{config.Source.Port}");
            Log.Information("Source DB reachable: {Database}@{Host}:{Port}",
                config.Source.Database, config.Source.Host, config.Source.Port);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cannot connect to source DB {Database}@{Host}:{Port}",
                config.Source.Database, config.Source.Host, config.Source.Port);
            throw new InvalidOperationException(
                $"Cannot connect to source DB ({config.Source.Database}@{config.Source.Host}:{config.Source.Port}): {ex.Message}", ex);
        }

        // ── 2. Connectivity: target ──────────────────────────────────────────
        await using var targetConn = new NpgsqlConnection(config.Target.ToConnectionString());
        try
        {
            await targetConn.OpenAsync(ct);
            AnsiConsole.MarkupLine(
                $"[green]✓ Target DB reachable:[/] {config.Target.Database}@{config.Target.Host}:{config.Target.Port}");
            Log.Information("Target DB reachable: {Database}@{Host}:{Port}",
                config.Target.Database, config.Target.Host, config.Target.Port);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cannot connect to target DB {Database}@{Host}:{Port}",
                config.Target.Database, config.Target.Host, config.Target.Port);
            throw new InvalidOperationException(
                $"Cannot connect to target DB ({config.Target.Database}@{config.Target.Host}:{config.Target.Port}): {ex.Message}", ex);
        }

        // ── 3. Read permissions on source (tables + sequences) ──────────────
        // Uses has_table_privilege / has_sequence_privilege so role-based grants
        // (e.g. pg_read_all_data) are respected, not just direct grants.
        await using (var cmd = sourceConn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                  (SELECT count(*) FROM pg_class c
                   JOIN pg_namespace n ON n.oid = c.relnamespace
                   WHERE n.nspname = 'public' AND c.relkind = 'r') AS total_tables,
                  (SELECT count(*) FROM pg_class c
                   JOIN pg_namespace n ON n.oid = c.relnamespace
                   WHERE n.nspname = 'public' AND c.relkind = 'r'
                     AND has_table_privilege(current_user, c.oid, 'SELECT')) AS accessible_tables,
                  (SELECT count(*) FROM pg_class c
                   JOIN pg_namespace n ON n.oid = c.relnamespace
                   WHERE n.nspname = 'public' AND c.relkind = 'S') AS total_sequences,
                  (SELECT count(*) FROM pg_class c
                   JOIN pg_namespace n ON n.oid = c.relnamespace
                   WHERE n.nspname = 'public' AND c.relkind = 'S'
                     AND has_sequence_privilege(current_user, c.oid, 'SELECT')) AS accessible_sequences
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var totalTables      = reader.GetInt64(0);
            var accessibleTables = reader.GetInt64(1);
            var totalSeqs        = reader.GetInt64(2);
            var accessibleSeqs   = reader.GetInt64(3);

            if (accessibleTables == 0 && totalTables > 0)
            {
                Log.Error("User {User} has no SELECT privilege on any table in source DB", config.Source.User);
                throw new InvalidOperationException(
                    $"User '{config.Source.User}' has no SELECT privilege on any table in source DB. " +
                    $"Run: GRANT pg_read_all_data TO \"{config.Source.User}\";");
            }

            if (accessibleTables < totalTables)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]⚠ PRE-FLIGHT[/] Source: only {accessibleTables}/{totalTables} table(s) readable — " +
                    $"pg_dump may fail. Run: [grey]GRANT pg_read_all_data TO \"{config.Source.User}\";[/]");
                Log.Warning("Source: only {Accessible}/{Total} table(s) readable — pg_dump may fail",
                    accessibleTables, totalTables);
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]✓ Source DB table access confirmed ({accessibleTables}/{totalTables} table(s))[/]");
                Log.Information("Source DB table access confirmed ({Accessible}/{Total})", accessibleTables, totalTables);
            }

            if (totalSeqs > 0 && accessibleSeqs < totalSeqs)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]⚠ PRE-FLIGHT[/] Source: only {accessibleSeqs}/{totalSeqs} sequence(s) readable — " +
                    $"pg_dump will fail on sequences. Run: [grey]GRANT pg_read_all_data TO \"{config.Source.User}\";[/]");
                Log.Warning("Source: only {Accessible}/{Total} sequence(s) readable — pg_dump will fail",
                    accessibleSeqs, totalSeqs);
            }
            else if (totalSeqs > 0)
            {
                AnsiConsole.MarkupLine($"[green]✓ Source DB sequence access confirmed ({accessibleSeqs}/{totalSeqs} sequence(s))[/]");
                Log.Information("Source DB sequence access confirmed ({Accessible}/{Total})", accessibleSeqs, totalSeqs);
            }
        }

        // ── 4. Write permission on target ─────────────────────────────────────
        // If target is empty (fresh restore destination) there are no tables yet —
        // skip the check and let pg_restore create them.
        await using (var cmd = targetConn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                  (SELECT count(*) FROM pg_class c
                   JOIN pg_namespace n ON n.oid = c.relnamespace
                   WHERE n.nspname = 'public' AND c.relkind = 'r') AS total_tables,
                  (SELECT count(*) FROM pg_class c
                   JOIN pg_namespace n ON n.oid = c.relnamespace
                   WHERE n.nspname = 'public' AND c.relkind = 'r'
                     AND has_table_privilege(current_user, c.oid, 'INSERT')) AS insertable_tables
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var totalTables      = reader.GetInt64(0);
            var insertableTables = reader.GetInt64(1);

            if (totalTables == 0)
            {
                AnsiConsole.MarkupLine("[green]✓ Target DB is empty — pg_restore will create tables[/]");
                Log.Information("Target DB is empty — pg_restore will create tables");
            }
            else if (insertableTables == 0)
            {
                Log.Error("User {User} has no INSERT privilege on any table in target DB", config.Target.User);
                throw new InvalidOperationException(
                    $"User '{config.Target.User}' has no INSERT privilege on any table in target DB. " +
                    $"Run: GRANT pg_write_all_data TO \"{config.Target.User}\";");
            }
            else if (insertableTables < totalTables)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]⚠ PRE-FLIGHT[/] Target: only {insertableTables}/{totalTables} table(s) writable. " +
                    $"Run: [grey]GRANT pg_write_all_data TO \"{config.Target.User}\";[/]");
                Log.Warning("Target: only {Insertable}/{Total} table(s) writable", insertableTables, totalTables);
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]✓ Target DB write access confirmed ({insertableTables}/{totalTables} table(s))[/]");
                Log.Information("Target DB write access confirmed ({Insertable}/{Total})", insertableTables, totalTables);
            }
        }

        // ── 5. Idle-in-transaction connections on source ─────────────────────
        // These hold table locks and will cause pg_dump to hang.
        await using (var cmd = sourceConn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT count(*), max(now() - query_start)
                FROM pg_stat_activity
                WHERE state = 'idle in transaction'
                  AND pid <> pg_backend_pid()
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var count = reader.GetInt64(0);
                if (count > 0)
                {
                    var maxAge = reader.IsDBNull(1) ? TimeSpan.Zero : reader.GetTimeSpan(1);
                    AnsiConsole.MarkupLine(
                        $"[yellow]⚠ PRE-FLIGHT[/] {count} connection(s) are [bold]idle in transaction[/] " +
                        $"(oldest: {maxAge:g}) — these may block pg_dump. " +
                        $"Run: [grey]SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
                        $"WHERE state = 'idle in transaction' AND pid <> pg_backend_pid();[/]");
                    Log.Warning("{Count} idle-in-transaction connection(s) detected (oldest: {MaxAge:g}) — may block pg_dump",
                        count, maxAge);
                }
            }
        }

        // ── 6. Ungranted locks on source ─────────────────────────────────────
        await using (var cmd = sourceConn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT count(*)
                FROM pg_locks
                WHERE granted = false
                """;
            var ungrantedLocks = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
            if (ungrantedLocks > 0)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]⚠ PRE-FLIGHT[/] {ungrantedLocks} ungranted lock(s) detected — " +
                    "there is active lock contention on the source DB.");
                Log.Warning("{Count} ungranted lock(s) detected on source DB", ungrantedLocks);
            }
        }

    }

    private void CleanupDump(string dumpPath, bool isDirectory)
    {
        try
        {
            if (isDirectory && Directory.Exists(dumpPath))
            {
                Directory.Delete(dumpPath, recursive: true);
                logger.LogDebug("Temp dump directory deleted: {Path}", dumpPath);
            }
            else if (!isDirectory && File.Exists(dumpPath))
            {
                File.Delete(dumpPath);
                logger.LogDebug("Temp dump file deleted: {Path}", dumpPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete temp dump at {Path} — you may need to remove it manually", dumpPath);
        }
    }

    private async Task RunProcessAsync(
        string executable,
        List<string> args,
        Dictionary<string, string> env,
        Action<string> onStderrLine,
        CancellationToken ct,
        int? warningExitCode = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in env)
            psi.Environment[key] = value;

        var argLine = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        if (PrintArgs)
            AnsiConsole.MarkupLine($"[grey]▸ {Markup.Escape(executable)} {Markup.Escape(argLine)}[/]");

        using var process = new Process { StartInfo = psi };

        // Buffer stderr for failure diagnostics; also stream each line to the spinner callback
        // and log at debug level immediately so --verbose shows progress without spamming ERR.
        var stderrLines = new System.Collections.Concurrent.ConcurrentQueue<string>();

        process.OutputDataReceived += (_, e) => { /* stdout consumed to prevent buffer blocking */ };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 })
            {
                stderrLines.Enqueue(e.Data);
                logger.LogDebug("[{Exe}] {Line}", executable, e.Data);   // file sink captures this; no console sink so it stays silent
                onStderrLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ProcessTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"'{executable}' exceeded the timeout of {ProcessTimeout.TotalMinutes:0} minutes.");
        }

        if (process.ExitCode != 0)
        {
            if (process.ExitCode == warningExitCode)
            {
                // Expected non-fatal exit code (e.g. pg_restore code 1 = ignored errors from --clean)
                var lastLine = stderrLines.LastOrDefault();
                if (lastLine is not null)
                    AnsiConsole.MarkupLine($"[yellow]⚠ {Markup.Escape(executable)}:[/] {Markup.Escape(lastLine)}");
                Log.Warning("{Exe} exited with code {Code} (non-fatal): {Message}",
                    executable, process.ExitCode, lastLine ?? "no output");
                return;
            }

            // Log the tail of stderr as errors — verbose output fills the buffer but the real
            // failure reason is always at the end. Cap at 20 lines to avoid spamming the log.
            var errorTail = stderrLines.ToArray();
            var start = Math.Max(0, errorTail.Length - 20);
            for (var i = start; i < errorTail.Length; i++)
                logger.LogError("[{Exe}] {Line}", executable, errorTail[i]);

            var lastError = errorTail.Length > 0 ? errorTail[^1] : "no output captured";
            throw new InvalidOperationException(
                $"'{executable}' exited with code {process.ExitCode}: {lastError}");
        }
    }

    private static string Truncate(string line) =>
        line.Length > 90 ? string.Concat(line.AsSpan(0, 90), "…") : line;
}
