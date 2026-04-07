using DbMigrator.Migration;
using DbMigrator.Sanitization;
using DbMigrator.Validation;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DbMigrator.Commands;

public class RunCommand(IServiceProvider services) : AsyncCommand<BaseSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, BaseSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = CommandHelpers.LoadAndValidateConfig(settings);

            if (settings.PreflightOnly)
            {
                await Migrator.RunPreflightChecksAsync(config.Migration, cancellationToken);
                AnsiConsole.MarkupLine("[green]✓ Pre-flight checks passed[/]");
                return 0;
            }

            var validator = services.GetRequiredService<SchemaValidator>();
            var ok = await CommandHelpers.RunSchemaValidationAsync(config, settings, validator, cancellationToken);
            if (!ok) return 1;

            if (!settings.DryRun)
            {
                AnsiConsole.MarkupLine("[bold blue]▶ Starting migration...[/]");
                var migrator = services.GetRequiredService<Migrator>();
                migrator.Verbose = settings.IsDebug;
                migrator.PrintArgs = settings.PrintArgs;
                await migrator.RunAsync(config.Migration, cancellationToken);
                AnsiConsole.MarkupLine("[green]✓ Migration completed[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]⚠ DRY RUN[/] Skipping migration step");
            }

            AnsiConsole.MarkupLine("[bold blue]▶ Starting sanitization...[/]");
            var sanitizer = services.GetRequiredService<Sanitizer>();
            var report = await sanitizer.RunAsync(config, settings.DryRun, cancellationToken);

            AnsiConsole.MarkupLine("[green]✓ Sanitization completed[/]");
            CommandHelpers.PrintReport(report);

            return 0;
        }
        catch (Exception ex)
        {
            CommandHelpers.PrintError(ex, settings);
            return 1;
        }
    }
}
