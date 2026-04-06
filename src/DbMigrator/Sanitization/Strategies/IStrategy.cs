using DbMigrator.Config;

namespace DbMigrator.Sanitization.Strategies;

/// <summary>
/// A sanitization strategy generates the SQL expression and parameters
/// for a single column update. Returning null from BuildSqlExpression
/// means SET column = NULL.
/// </summary>
public interface IStrategy
{
    /// <summary>
    /// Returns the SQL expression to use in the SET clause (e.g. "@p_email")
    /// and populates <paramref name="parameters"/> with any needed values.
    /// Return null to produce SET column = NULL.
    /// </summary>
    string? BuildSqlExpression(ColumnRule rule, string paramName, IDictionary<string, object?> parameters);
}
