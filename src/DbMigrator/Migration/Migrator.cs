using System.Diagnostics;
using DbMigrator.Config;
using Microsoft.Extensions.Logging;
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

    public async Task RunAsync(MigrationConfig config, CancellationToken ct = default)
    {
        bool parallel = config.Dump.ParallelJobs > 1;
        var dumpPath = parallel
            ? Path.Combine(Path.GetTempPath(), $"db-migrator-dump-{Guid.NewGuid():N}")
            : Path.Combine(Path.GetTempPath(), $"db-migrator-dump-{Guid.NewGuid():N}.dump");

        try
        {
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
                        await DumpAsync(config, dumpPath,
                            line => ctx.Status($"[cyan]pg_dump[/] {Markup.Escape(Truncate(line))}"),
                            ct);
                    });
            sw.Stop();

            long dumpSize = parallel
                ? new DirectoryInfo(dumpPath).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                : new FileInfo(dumpPath).Length;

            logger.LogInformation(
                "Dump completed in {Elapsed:g} ({Size:N0} bytes). Restoring to {Database}@{Host}:{Port}",
                sw.Elapsed, dumpSize, config.Target.Database, config.Target.Host, config.Target.Port);

            sw.Restart();
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync(
                    $"[cyan]pg_restore[/] → {config.Target.Database}@{config.Target.Host}:{config.Target.Port} ...",
                    async ctx =>
                    {
                        await RestoreAsync(config, dumpPath,
                            line => ctx.Status($"[cyan]pg_restore[/] {Markup.Escape(Truncate(line))}"),
                            ct);
                    });
            sw.Stop();

            logger.LogInformation("Restore completed in {Elapsed:g}", sw.Elapsed);
        }
        finally
        {
            CleanupDump(dumpPath, parallel);
        }
    }

    private async Task DumpAsync(
        MigrationConfig config, string dumpPath, Action<string> onProgress, CancellationToken ct)
    {
        var args = BuildPgDumpArgs(config, dumpPath, Verbose);
        var env = new Dictionary<string, string> { ["PGPASSWORD"] = config.Source.Password };
        await RunProcessAsync("pg_dump", args, env, onProgress, ct);
    }

    private async Task RestoreAsync(
        MigrationConfig config, string dumpPath, Action<string> onProgress, CancellationToken ct)
    {
        var args = BuildPgRestoreArgs(config, dumpPath, Verbose);
        var env = new Dictionary<string, string> { ["PGPASSWORD"] = config.Target.Password };
        await RunProcessAsync("pg_restore", args, env, onProgress, ct);
    }

    public static List<string> BuildPgDumpArgs(MigrationConfig config, string dumpPath, bool verbose = false)
    {
        bool parallel = config.Dump.ParallelJobs > 1;
        var args = new List<string>
        {
            "--host",     config.Source.Host,
            "--port",     config.Source.Port.ToString(),
            "--username", config.Source.User,
            "--format",   parallel ? "directory" : "custom",
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

    public static List<string> BuildPgRestoreArgs(MigrationConfig config, string dumpPath, bool verbose = false)
    {
        bool parallel = config.Dump.ParallelJobs > 1;
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
        CancellationToken ct)
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

        // Always log full args at debug level; print to console when --print-args is set
        var argLine = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        logger.LogDebug("Executing: {Exe} {Args}", executable, argLine);
        if (PrintArgs)
            AnsiConsole.MarkupLine($"[grey]▸ {Markup.Escape(executable)} {Markup.Escape(argLine)}[/]");

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                logger.LogDebug("[{Exe}] {Line}", executable, e.Data);
        };

        // pg_dump / pg_restore write progress to stderr when --verbose is set.
        // Route lines to the spinner callback only — do NOT also call logger.LogDebug here,
        // because Serilog writes directly to stdout and conflicts with Spectre.Console's
        // cursor management, producing a visual gap in the output.
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 })
                onStderrLine(e.Data);
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
            throw new InvalidOperationException(
                $"'{executable}' exited with code {process.ExitCode}. Check the logs above for details.");
    }

    private static string Truncate(string line) =>
        line.Length > 90 ? string.Concat(line.AsSpan(0, 90), "…") : line;
}
