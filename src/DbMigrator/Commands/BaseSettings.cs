using System.ComponentModel;
using Spectre.Console.Cli;

namespace DbMigrator.Commands;

public class BaseSettings : CommandSettings
{
    [CommandOption("-c|--config <PATH>")]
    [Description("Path to the YAML configuration file")]
    [DefaultValue("config.yaml")]
    public string ConfigPath { get; set; } = "config.yaml";

    [CommandOption("--dry-run")]
    [Description("Show what would be done without executing")]
    public bool DryRun { get; set; }

    [CommandOption("--skip-validation")]
    [Description("Skip pre-execution schema validation against source DB")]
    public bool SkipValidation { get; set; }

    [CommandOption("--strict")]
    [Description("Treat validation warnings as errors")]
    public bool Strict { get; set; }

    [CommandOption("--log-level <LEVEL>")]
    [Description("Log level: debug, info, warn, error")]
    [DefaultValue("info")]
    public string LogLevel { get; set; } = "info";

    [CommandOption("--print-args")]
    [Description("Print the pg_dump / pg_restore command arguments before executing")]
    public bool PrintArgs { get; set; }

    [CommandOption("--verbose|-v")]
    [Description("Shorthand for --log-level debug")]
    public bool Verbose { get; set; }

    public bool IsDebug => Verbose || LogLevel.Equals("debug", StringComparison.OrdinalIgnoreCase);
}
