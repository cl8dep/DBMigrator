using DbMigrator.Config;
using DbMigrator.Validation;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DbMigrator.Commands;

public class ValidateCommand(IServiceProvider services) : AsyncCommand<BaseSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, BaseSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = CommandHelpers.LoadAndValidateConfig(settings);
            AnsiConsole.MarkupLine("[green]✓ Config structure is valid[/]");

            if (settings.SkipValidation)
            {
                AnsiConsole.MarkupLine("[yellow]⚠[/] Schema validation skipped (--skip-validation)");
                return 0;
            }

            var validator = services.GetRequiredService<SchemaValidator>();
            var ok = await CommandHelpers.RunSchemaValidationAsync(config, settings, validator, cancellationToken);

            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            CommandHelpers.PrintError(ex, settings);
            return 1;
        }
    }
}
