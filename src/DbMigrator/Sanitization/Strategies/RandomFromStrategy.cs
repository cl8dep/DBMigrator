using DbMigrator.Config;

namespace DbMigrator.Sanitization.Strategies;

/// <summary>
/// Picks a random value from a user-defined list on each row.
/// Useful for categorical fields like status, role, country_code, plan, etc.
///
/// Config example:
///   strategy: random_from
///   values: ["active", "inactive", "pending"]
///
/// Generated SQL (for 3 values):
///   (ARRAY[@p_status_0, @p_status_1, @p_status_2])[floor(random() * 3)::int + 1]
///
/// PostgreSQL arrays are 1-indexed; floor(random() * N) gives 0..N-1, +1 makes it 1..N.
/// </summary>
public class RandomFromStrategy : IStrategy
{
    public string? BuildSqlExpression(ColumnRule rule, string paramName, IDictionary<string, object?> parameters)
    {
        var values = rule.Values!;
        var paramNames = new List<string>(values.Count);

        for (var i = 0; i < values.Count; i++)
        {
            var p = $"{paramName}_{i}";
            parameters[p] = values[i];
            paramNames.Add($"@{p}");
        }

        var array = $"ARRAY[{string.Join(", ", paramNames)}]";
        return $"({array})[floor(random() * {values.Count})::int + 1]";
    }
}
