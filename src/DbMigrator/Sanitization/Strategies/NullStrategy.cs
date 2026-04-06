using DbMigrator.Config;

namespace DbMigrator.Sanitization.Strategies;

/// <summary>Sets every row to NULL.</summary>
public class NullStrategy : IStrategy
{
    public string? BuildSqlExpression(ColumnRule rule, string paramName, IDictionary<string, object?> parameters)
        => null; // null return → SET column = NULL
}
