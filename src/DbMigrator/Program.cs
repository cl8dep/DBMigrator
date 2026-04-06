using DbMigrator.Commands;
using DbMigrator.Infrastructure;
using DbMigrator.Migration;
using DbMigrator.Sanitization;
using DbMigrator.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Spectre.Console.Cli;

// Parse log level early from args before DI is built
var logLevel = args.Contains("--log-level")
    ? args.SkipWhile(a => a != "--log-level").Skip(1).FirstOrDefault() ?? "info"
    : "info";

var serilogLevel = logLevel.ToLowerInvariant() switch
{
    "debug" => LogEventLevel.Debug,
    "warn"  => LogEventLevel.Warning,
    "error" => LogEventLevel.Error,
    _       => LogEventLevel.Information
};

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(serilogLevel)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

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
