using DbMigrator.Config;

namespace DbMigrator.Sanitization.Strategies;

/// <summary>Sets every row to the same fixed value.</summary>
public class StaticStrategy : IStrategy
{
    public string? BuildSqlExpression(ColumnRule rule, string paramName, IDictionary<string, object?> parameters)
    {
        parameters[paramName] = rule.Value;
        return $"@{paramName}";
    }
}
