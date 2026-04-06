using DbMigrator.Config;
using DbMigrator.Validation;
using Spectre.Console;

namespace DbMigrator.Commands;

internal static class CommandHelpers
{
    public static AppConfig LoadAndValidateConfig(BaseSettings settings)
    {
        AnsiConsole.MarkupLine($"[grey]Loading config from:[/] {settings.ConfigPath}");
        var config = ConfigLoader.Load(settings.ConfigPath);

        var structuralErrors = ConfigLoader.ValidateStructure(config);
        if (structuralErrors.Count > 0)
        {
            foreach (var err in structuralErrors)
                AnsiConsole.MarkupLine($"[red]✗ CONFIG ERROR[/] {err}");
            throw new InvalidOperationException("Config validation failed. Fix the errors above.");
        }

        return config;
    }

    public static async Task<bool> RunSchemaValidationAsync(
        AppConfig config,
        BaseSettings settings,
        SchemaValidator validator,
        CancellationToken ct)
    {
        if (settings.SkipValidation)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ SKIP[/] Schema validation skipped (--skip-validation)");
            return true;
        }

        AnsiConsole.MarkupLine("[grey]Running schema validation against source DB...[/]");
        var result = await validator.ValidateAsync(config, ct);

        foreach (var warning in result.Warnings)
            AnsiConsole.MarkupLine($"[yellow]⚠ WARNING[/] {Markup.Escape(warning.Message)}");

        foreach (var error in result.Errors)
            AnsiConsole.MarkupLine($"[red]✗ ERROR[/]   {Markup.Escape(error.Message)}");

        var hasBlockingIssues = result.HasErrors || (settings.Strict && result.Warnings.Any());

        if (hasBlockingIssues)
        {
            var errorCount = result.Errors.Count();
            var warnCount = result.Warnings.Count();
            AnsiConsole.MarkupLine(
                $"[red]Validation failed with {errorCount} error(s) and {warnCount} warning(s). " +
                $"Fix errors before proceeding.[/]");
            return false;
        }

        if (result.Issues.Count == 0)
            AnsiConsole.MarkupLine("[green]✓ Schema validation passed[/]");

        return true;
    }

    public static void PrintError(Exception ex, BaseSettings settings)
    {
        AnsiConsole.MarkupLine($"[red]✗ Fatal error:[/] {Markup.Escape(ex.Message)}");
        if (settings.IsDebug)
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(ex.ToString())}[/]");
    }

    public static void PrintReport(Sanitization.SanitizationReport report)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Table")
            .AddColumn(new TableColumn("Rows Updated").RightAligned());

        foreach (var r in report.Results)
            table.AddRow(r.Table, r.RowsAffected.ToString());

        table.AddRow("[bold]TOTAL[/]", $"[bold]{report.TotalRowsAffected}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
    }
}
