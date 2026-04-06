using DbMigrator.Sanitization;
using DbMigrator.Validation;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DbMigrator.Commands;

public class SanitizeCommand(IServiceProvider services) : AsyncCommand<BaseSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, BaseSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = CommandHelpers.LoadAndValidateConfig(settings);

            var validator = services.GetRequiredService<SchemaValidator>();
            var ok = await CommandHelpers.RunSchemaValidationAsync(config, settings, validator, cancellationToken);
            if (!ok) return 1;

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
