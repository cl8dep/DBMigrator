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

    [CommandOption("--no-color")]
    [Description("Disable ANSI color codes in output (also respected via NO_COLOR env var)")]
    public bool NoColor { get; set; }

    [CommandOption("--preflight-only")]
    [Description("Run pre-flight checks only (connectivity, permissions, locks) and exit")]
    public bool PreflightOnly { get; set; }

    [CommandOption("--gcp-logs")]
    [Description("Output structured JSON logs for Google Cloud Logging (auto-enabled in Cloud Run)")]
    public bool GcpLogs { get; set; }

    [CommandOption("--log-file <PATH>")]
    [Description("Also write logs to a file at the specified path")]
    public string? LogFile { get; set; }

    public bool IsDebug => Verbose || LogLevel.Equals("debug", StringComparison.OrdinalIgnoreCase);
}
