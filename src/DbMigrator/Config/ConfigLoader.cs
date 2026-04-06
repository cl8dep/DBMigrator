using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DbMigrator.Config;

public static class ConfigLoader
{
    private static readonly Regex EnvVarPattern = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    // Identifiers must be alphanumeric + underscore only. Prevents quote-injection in SQL.
    private static readonly Regex SafeIdentifier = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    private static readonly HashSet<string> KnownStrategies = new(StringComparer.OrdinalIgnoreCase)
    {
        "static", "null_value", "template", "faker", "hash"
    };

    private static readonly HashSet<string> ValidSslModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Disable", "Allow", "Prefer", "Require", "VerifyCA", "VerifyFull"
    };

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Config file not found: {path}");

        var raw = File.ReadAllText(path);
        var interpolated = InterpolateEnvVars(raw);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var config = deserializer.Deserialize<AppConfig>(interpolated);
        return config;
    }

    public static string InterpolateEnvVars(string input)
    {
        // Process line by line so YAML comments (# ...) are never interpolated
        var lines = input.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith('#'))
                continue; // skip comment lines entirely

            lines[i] = EnvVarPattern.Replace(lines[i], match =>
            {
                var varName = match.Groups[1].Value;
                var value = Environment.GetEnvironmentVariable(varName);
                if (value is null)
                    throw new InvalidOperationException(
                        $"Environment variable '{varName}' is referenced in config but not set.");
                return value;
            });
        }

        return string.Join('\n', lines);
    }

    public static List<string> ValidateStructure(AppConfig config)
    {
        var errors = new List<string>();

        ValidateDbConfig(config.Migration.Source, "migration.source", errors);
        ValidateDbConfig(config.Migration.Target, "migration.target", errors);

        // Guard: source and target must not be the same DB (would destroy source data)
        if (!string.IsNullOrWhiteSpace(config.Migration.Source.Database) &&
            !string.IsNullOrWhiteSpace(config.Migration.Target.Database) &&
            config.Migration.Source.Host == config.Migration.Target.Host &&
            config.Migration.Source.Port == config.Migration.Target.Port &&
            config.Migration.Source.Database == config.Migration.Target.Database)
        {
            errors.Add("migration.source and migration.target point to the same database — " +
                       "this would overwrite your source data. Use different databases.");
        }

        if (config.Migration.Dump.ParallelJobs < 1)
            errors.Add("migration.dump.parallel_jobs must be >= 1 (default is 1 for single-threaded)");

        foreach (var rule in config.Sanitize)
        {
            if (string.IsNullOrWhiteSpace(rule.Table))
            {
                errors.Add("A sanitize rule has an empty table name");
                continue;
            }

            if (!SafeIdentifier.IsMatch(rule.Table))
                errors.Add($"Table name '{rule.Table}' contains invalid characters. " +
                           "Only letters, digits, and underscores are allowed.");

            foreach (var col in rule.Columns)
            {
                if (string.IsNullOrWhiteSpace(col.Name))
                {
                    errors.Add($"A column rule in table '{rule.Table}' has an empty name");
                    continue;
                }

                if (!SafeIdentifier.IsMatch(col.Name))
                    errors.Add($"Column name '{rule.Table}.{col.Name}' contains invalid characters. " +
                               "Only letters, digits, and underscores are allowed.");

                if (!KnownStrategies.Contains(col.Strategy))
                    errors.Add($"Unknown strategy '{col.Strategy}' for column '{rule.Table}.{col.Name}'. " +
                               $"Valid values: {string.Join(", ", KnownStrategies)}");

                if (col.Strategy.Equals("faker", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(col.Faker))
                    errors.Add($"Column '{rule.Table}.{col.Name}' uses strategy 'faker' but 'faker' field is missing");

                if (col.Strategy.Equals("template", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(col.Value))
                    errors.Add($"Column '{rule.Table}.{col.Name}' uses strategy 'template' but 'value' field is missing");
            }
        }

        return errors;
    }

    private static void ValidateDbConfig(DbConfig db, string prefix, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(db.Host))
            errors.Add($"{prefix}.host is required");

        if (string.IsNullOrWhiteSpace(db.Database))
            errors.Add($"{prefix}.database is required");

        if (db.Port is < 1 or > 65535)
            errors.Add($"{prefix}.port must be between 1 and 65535 (got {db.Port})");

        if (!ValidSslModes.Contains(db.SslMode))
            errors.Add($"{prefix}.ssl_mode '{db.SslMode}' is invalid. " +
                       $"Valid values: {string.Join(", ", ValidSslModes)}");

        if (!SafeIdentifier.IsMatch(db.Schema))
            errors.Add($"{prefix}.schema '{db.Schema}' contains invalid characters");
    }
}
