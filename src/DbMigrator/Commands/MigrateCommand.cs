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

            var validator = services.GetRequiredService<SchemaValidator>();
            var ok = await CommandHelpers.RunSchemaValidationAsync(config, settings, validator, cancellationToken);
            if (!ok) return 1;

            if (settings.DryRun)
            {
                AnsiConsole.MarkupLine("[yellow]⚠ DRY RUN[/] Would run pg_dump + pg_restore but skipping");
                return 0;
            }

            AnsiConsole.MarkupLine("[bold blue]▶ Starting migration...[/]");
            var migrator = services.GetRequiredService<Migrator>();
            await migrator.RunAsync(config.Migration, cancellationToken);
            AnsiConsole.MarkupLine("[green]✓ Migration completed[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Fatal error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
