using DbMigrator.Config;

namespace DbMigrator.Sanitization.Strategies;

/// <summary>
/// Masks a column value in-place using SQL string functions.
/// Keeps <c>keep_first</c> leading characters and/or <c>keep_last</c> trailing characters
/// visible; replaces the middle portion with <c>mask_char</c> (default '*') repeated to
/// fill the same length, so the total character count is preserved.
///
/// Example (keep_first: 3, keep_last: 2, mask_char: '*'):
///   "john.doe@example.com"  →  "joh**************om"
///
/// The expression is pure SQL so no value is read from the DB into the application.
/// </summary>
public class PartialMaskStrategy : IStrategy
{
    public string? BuildSqlExpression(ColumnRule rule, string paramName, IDictionary<string, object?> parameters)
    {
        var keepFirst = rule.KeepFirst ?? 0;
        var keepLast  = rule.KeepLast  ?? 0;
        var maskChar  = string.IsNullOrEmpty(rule.MaskChar) ? "*" : rule.MaskChar[0].ToString();

        // Parameterise the mask character to avoid any injection risk.
        var maskCharParam = $"{paramName}_mc";
        parameters[maskCharParam] = maskChar;

        var col = $"\"{rule.Name}\"";
        var lenExpr = $"LENGTH({col}::text)";

        // Number of characters to mask = total length − kept head − kept tail (floor at 0)
        var maskedLen = $"GREATEST(0, {lenExpr} - {keepFirst} - {keepLast})";

        // Build the three segments (each segment is omitted when its length is 0)
        var parts = new List<string>();

        if (keepFirst > 0)
            parts.Add($"LEFT({col}::text, {keepFirst})");

        parts.Add($"REPEAT(@{maskCharParam}, {maskedLen})");

        if (keepLast > 0)
            parts.Add($"RIGHT({col}::text, {keepLast})");

        return parts.Count == 1
            ? parts[0]
            : $"({string.Join(" || ", parts)})";
    }
}
