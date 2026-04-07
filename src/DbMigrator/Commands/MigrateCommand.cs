using DbMigrator.Migration;
using DbMigrator.Validation;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DbMigrator.Commands;

public class MigrateCommand(IServiceProvider services) : AsyncCommand<BaseSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, BaseSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = CommandHelpers.LoadAndValidateConfig(settings);

            // ── 1. Pre-flight checks (always) ────────────────────────────────
            await Migrator.RunPreflightChecksAsync(config.Migration, cancellationToken);
            AnsiConsole.MarkupLine("[green]✓ Pre-flight checks passed[/]");

            if (settings.PreflightOnly)
                return 0;

            // ── 2. Schema validation ─────────────────────────────────────────
            var validator = services.GetRequiredService<SchemaValidator>();
            var ok = await CommandHelpers.RunSchemaValidationAsync(config, settings, validator, cancellationToken);
            if (!ok) return 1;

            // ── 3. Migration ─────────────────────────────────────────────────
            if (settings.DryRun)
            {
                AnsiConsole.MarkupLine("[yellow]⚠ DRY RUN[/] Would run pg_dump + pg_restore but skipping");
                return 0;
            }

            AnsiConsole.MarkupLine("[bold blue]▶ Starting migration...[/]");
            var migrator = services.GetRequiredService<Migrator>();
            migrator.Verbose = settings.IsDebug;
            migrator.PrintArgs = settings.PrintArgs;
            await migrator.RunAsync(config.Migration, cancellationToken);
            AnsiConsole.MarkupLine("[green]✓ Migration completed[/]");

            return 0;
        }
        catch (Exception ex)
        {
            CommandHelpers.PrintError(ex, settings);
            return 1;
        }
    }
}
