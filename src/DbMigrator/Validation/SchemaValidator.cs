using DbMigrator.Config;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DbMigrator.Validation;

public class SchemaValidator(ILogger<SchemaValidator> logger)
{
    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "integer", "bigint", "smallint", "numeric", "real", "double precision",
        "decimal", "int", "int2", "int4", "int8", "float4", "float8"
    };

    private static readonly HashSet<string> FakerNumericMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "random_number", "random_double", "digits_as_integer"
    };

    public async Task<ValidationResult> ValidateAsync(AppConfig config, CancellationToken ct = default)
    {
        var result = new ValidationResult();

        if (config.Sanitize.Count == 0)
        {
            logger.LogInformation("No sanitize rules defined — skipping schema validation");
            return result;
        }

        await using var conn = new NpgsqlConnection(config.Migration.Source.ToConnectionString());

        try
        {
            await conn.OpenAsync(ct);
        }
        catch (Exception ex)
        {
            result.AddError($"Cannot connect to source database for validation: {ex.Message}");
            return result;
        }

        var schema = config.Migration.Source.Schema;

        foreach (var rule in config.Sanitize)
        {
            await ValidateTableAsync(conn, rule, schema, result, ct);
        }

        return result;
    }

    private async Task ValidateTableAsync(
        NpgsqlConnection conn,
        SanitizeRule rule,
        string schema,
        ValidationResult result,
        CancellationToken ct)
    {
        var tableExists = await TableExistsAsync(conn, schema, rule.Table, ct);
        if (!tableExists)
        {
            result.AddError($"Table '{schema}.{rule.Table}' does not exist in the source database");
            return;
        }

        var rowCount = await GetRowCountAsync(conn, schema, rule.Table, ct);
        if (rowCount == 0)
            result.AddWarning($"Table '{schema}.{rule.Table}' exists but has 0 rows — sanitization will have no effect");

        var columns = await GetColumnInfoAsync(conn, schema, rule.Table, ct);
        var primaryKeys = await GetPrimaryKeysAsync(conn, schema, rule.Table, ct);

        foreach (var colRule in rule.Columns)
        {
            if (!columns.TryGetValue(colRule.Name, out var colInfo))
            {
                result.AddError($"Column '{rule.Table}.{colRule.Name}' does not exist in the source database");
                continue;
            }

            var isNotNull = colInfo.IsNullable.Equals("NO", StringComparison.OrdinalIgnoreCase);

            if (colRule.Strategy.Equals("null_value", StringComparison.OrdinalIgnoreCase) && isNotNull)
                result.AddError(
                    $"Column '{rule.Table}.{colRule.Name}' is NOT NULL but strategy is 'null_value'");

            if (colRule.Strategy.Equals("static", StringComparison.OrdinalIgnoreCase) &&
                colRule.Value is null && isNotNull)
                result.AddError(
                    $"Column '{rule.Table}.{colRule.Name}' is NOT NULL but 'value' is null with strategy 'static'");

            if (primaryKeys.Contains(colRule.Name))
                result.AddWarning(
                    $"Column '{rule.Table}.{colRule.Name}' is a PRIMARY KEY — modifying it may break FK constraints");

            if (colRule.Strategy.Equals("faker", StringComparison.OrdinalIgnoreCase) && colRule.Faker is not null)
            {
                var isNumericColumn = NumericTypes.Contains(colInfo.DataType);
                var isFakerNumeric = FakerNumericMethods.Contains(colRule.Faker);
                if (isNumericColumn && !isFakerNumeric)
                    result.AddWarning(
                        $"Column '{rule.Table}.{colRule.Name}' is of type '{colInfo.DataType}' (numeric) " +
                        $"but faker method '{colRule.Faker}' may produce a non-numeric value");
            }
        }
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection conn, string schema, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM information_schema.tables
            WHERE table_schema = @schema AND table_name = @tableName
            """;
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("tableName", table);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }

    private static async Task<long> GetRowCountAsync(
        NpgsqlConnection conn, string schema, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        // Table and schema names are already validated as safe identifiers by ConfigLoader
        cmd.CommandText = $"""SELECT COUNT(*) FROM "{schema}"."{table}" """;
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    private static async Task<Dictionary<string, ColumnInfo>> GetColumnInfoAsync(
        NpgsqlConnection conn, string schema, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT column_name, is_nullable, data_type
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @tableName
            """;
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("tableName", table);

        var columns = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            columns[name] = new ColumnInfo(
                IsNullable: reader.GetString(1),
                DataType: reader.GetString(2));
        }

        return columns;
    }

    private static async Task<HashSet<string>> GetPrimaryKeysAsync(
        NpgsqlConnection conn, string schema, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type = 'PRIMARY KEY'
                AND tc.table_schema = @schema
                AND tc.table_name = @tableName
            """;
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("tableName", table);

        var pks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            pks.Add(reader.GetString(0));

        return pks;
    }

    private record ColumnInfo(string IsNullable, string DataType);
}
