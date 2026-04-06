using System.Diagnostics;
using DbMigrator.Config;
using Microsoft.Extensions.Logging;

namespace DbMigrator.Migration;

public class Migrator(ILogger<Migrator> logger)
{
    /// <summary>Maximum time allowed for each subprocess (pg_dump or pg_restore).</summary>
    public TimeSpan ProcessTimeout { get; set; } = TimeSpan.FromHours(2);

    public async Task RunAsync(MigrationConfig config, CancellationToken ct = default)
    {
        var dumpPath = Path.Combine(Path.GetTempPath(), $"db-migrator-dump-{Guid.NewGuid():N}.dump");

        try
        {
            logger.LogInformation("Starting dump from {Database}@{Host}:{Port}",
                config.Source.Database, config.Source.Host, config.Source.Port);

            await DumpAsync(config, dumpPath, ct);

            logger.LogInformation("Dump completed ({Size} bytes). Starting restore to {Database}@{Host}:{Port}",
                new FileInfo(dumpPath).Length,
                config.Target.Database, config.Target.Host, config.Target.Port);

            await RestoreAsync(config, dumpPath, ct);

            logger.LogInformation("Restore completed successfully");
        }
        finally
        {
            if (File.Exists(dumpPath))
            {
                File.Delete(dumpPath);
                logger.LogDebug("Temp dump file deleted: {Path}", dumpPath);
            }
        }
    }

    private async Task DumpAsync(MigrationConfig config, string dumpPath, CancellationToken ct)
    {
        var args = BuildPgDumpArgs(config, dumpPath);
        var env = new Dictionary<string, string>
        {
            ["PGPASSWORD"] = config.Source.Password
        };

        await RunProcessAsync("pg_dump", args, env, ct);
    }

    private async Task RestoreAsync(MigrationConfig config, string dumpPath, CancellationToken ct)
    {
        var args = BuildPgRestoreArgs(config, dumpPath);
        var env = new Dictionary<string, string>
        {
            ["PGPASSWORD"] = config.Target.Password
        };

        await RunProcessAsync("pg_restore", args, env, ct);
    }

    public static List<string> BuildPgDumpArgs(MigrationConfig config, string dumpPath)
    {
        var args = new List<string>
        {
            "--host", config.Source.Host,
            "--port", config.Source.Port.ToString(),
            "--username", config.Source.User,
            "--format", "custom",
            "--no-password",
            "--file", dumpPath
        };

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

    public static List<string> BuildPgRestoreArgs(MigrationConfig config, string dumpPath)
    {
        var args = new List<string>
        {
            "--host", config.Target.Host,
            "--port", config.Target.Port.ToString(),
            "--username", config.Target.User,
            "--dbname", config.Target.Database,
            "--no-password",
            "--clean",
            "--if-exists",
            dumpPath
        };

        return args;
    }

    private async Task RunProcessAsync(
        string executable,
        List<string> args,
        Dictionary<string, string> env,
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

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                logger.LogDebug("[{Exe}] {Line}", executable, e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                logger.LogWarning("[{Exe}] {Line}", executable, e.Data);
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
                $"'{executable}' exited with code {process.ExitCode}. " +
                "Check the logs above for details.");
    }
}
