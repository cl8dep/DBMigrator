using DbMigrator.Config;

namespace DbMigrator.Sanitization.Strategies;

/// <summary>
/// Replaces {column_name} placeholders in the value template with the
/// actual column value from each row. The substitution happens in SQL
/// using string concatenation so no per-row round-trip is needed.
///
/// Example: "user_{id}@redacted.local"
/// Produces: SET email = 'user_' || id::text || '@redacted.local'
/// </summary>
public class TemplateStrategy : IStrategy
{
    public string? BuildSqlExpression(ColumnRule rule, string paramName, IDictionary<string, object?> parameters)
    {
        if (string.IsNullOrEmpty(rule.Value))
            throw new InvalidOperationException(
                $"Strategy 'template' on column '{rule.Name}' requires a 'value' field.");

        // Parse the template and build a SQL concatenation expression
        var parts = ParseTemplate(rule.Value);
        var sqlParts = parts.Select((part, i) =>
        {
            if (part.IsColumnRef)
                return $"\"{part.Text}\"::text";

            var pName = $"{paramName}_t{i}";
            parameters[pName] = part.Text;
            return $"@{pName}";
        });

        return string.Join(" || ", sqlParts);
    }

    public static List<TemplatePart> ParseTemplate(string template)
    {
        var parts = new List<TemplatePart>();
        var remaining = template;

        while (remaining.Length > 0)
        {
            var start = remaining.IndexOf('{');
            if (start < 0)
            {
                parts.Add(new TemplatePart(remaining, false));
                break;
            }

            if (start > 0)
                parts.Add(new TemplatePart(remaining[..start], false));

            var end = remaining.IndexOf('}', start);
            if (end < 0)
            {
                parts.Add(new TemplatePart(remaining[start..], false));
                break;
            }

            var colName = remaining[(start + 1)..end];
            parts.Add(new TemplatePart(colName, true));
            remaining = remaining[(end + 1)..];
        }

        return parts;
    }

    public record TemplatePart(string Text, bool IsColumnRef);
}
