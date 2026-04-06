using System.Security.Cryptography;
using System.Text;
using DbMigrator.Config;

namespace DbMigrator.Sanitization.Strategies;

/// <summary>
/// Replaces the column value with its SHA256 hex digest.
/// Deterministic: same input always produces same hash (useful for
/// referential integrity across tables).
///
/// Implemented as a SQL expression so no per-row round-trip is needed:
/// encode(sha256(column::text::bytea), 'hex')
/// </summary>
public class HashStrategy : IStrategy
{
    public string? BuildSqlExpression(ColumnRule rule, string paramName, IDictionary<string, object?> parameters)
    {
        // Pure SQL expression — no parameters needed
        return $"encode(sha256(\"{rule.Name}\"::text::bytea), 'hex')";
    }

    /// <summary>Utility for tests: hash a value the same way Postgres would.</summary>
    public static string HashValue(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
