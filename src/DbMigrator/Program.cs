using DbMigrator.Commands;
using DbMigrator.Infrastructure;
using DbMigrator.Migration;
using DbMigrator.Sanitization;
using DbMigrator.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Spectre.Console;
using Spectre.Console.Cli;

// Parse log level early from args before DI is built
var logLevel = args.Contains("--log-level")
    ? args.SkipWhile(a => a != "--log-level").Skip(1).FirstOrDefault() ?? "info"
    : args.Contains("--verbose") || args.Contains("-v") ? "debug" : "info";

var serilogLevel = logLevel.ToLowerInvariant() switch
{
    "debug" => LogEventLevel.Debug,
    "warn"  => LogEventLevel.Warning,
    "error" => LogEventLevel.Error,
    _       => LogEventLevel.Information
};

// Disable ANSI colors if --no-color flag or NO_COLOR env var is set (https://no-color.org/)
var noColor = args.Contains("--no-color")
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

if (noColor)
    AnsiConsole.Profile.Capabilities.Ansi = false;

// GCP structured JSON logging: enabled via --gcp-logs flag, or auto-detected in Cloud Run
// (Cloud Run always sets the K_SERVICE env var)
var gcpLogs = args.Contains("--gcp-logs")
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("K_SERVICE"));

// Optional file logging: --log-file <path>
var logFile = args.Contains("--log-file")
    ? args.SkipWhile(a => a != "--log-file").Skip(1).FirstOrDefault()
    : null;

var logConfig = new LoggerConfiguration().MinimumLevel.Is(serilogLevel);

if (gcpLogs)
{
    // Compact JSON (CLEF) — Cloud Logging parses @t/@m/@l fields natively.
    logConfig.WriteTo.Console(new RenderedCompactJsonFormatter());

    // Suppress AnsiConsole entirely — Serilog JSON owns stdout.
    // All user-facing messages are already mirrored via Log.* calls.
    AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(TextWriter.Null)
    });
}
// else: no Serilog console sink — AnsiConsole handles all interactive output cleanly.

if (logFile is not null)
    logConfig.WriteTo.File(
        logFile,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        rollingInterval: RollingInterval.Infinite,
        shared: false);

Log.Logger = logConfig.CreateLogger();

var services = new ServiceCollection();

services.AddLogging(b => b
    .ClearProviders()
    .AddSerilog(dispose: true));

services.AddSingleton<Migrator>();
services.AddSingleton<Sanitizer>();
services.AddSingleton<SchemaValidator>();

var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("db-migrator");
    config.SetApplicationVersion("1.0.0");

    config.AddCommand<RunCommand>("run")
        .WithDescription("Migrate from source to target and then sanitize (full pipeline)")
        .WithExample(["run", "--config", "config.yaml"]);

    config.AddCommand<MigrateCommand>("migrate")
        .WithDescription("Run only the pg_dump + pg_restore step")
        .WithExample(["migrate", "--config", "config.yaml"]);

    config.AddCommand<SanitizeCommand>("sanitize")
        .WithDescription("Run only the sanitization step on the target DB")
        .WithExample(["sanitize", "--config", "config.yaml"]);

    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("Validate config and check schema against source DB without executing")
        .WithExample(["validate", "--config", "config.yaml"]);
});

return await app.RunAsync(args);
