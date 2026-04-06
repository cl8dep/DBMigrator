using DbMigrator.Config;
using DbMigrator.Sanitization.Strategies;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DbMigrator.Sanitization;

public class Sanitizer(ILogger<Sanitizer> logger)
{
    public async Task<SanitizationReport> RunAsync(
        AppConfig config,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var report = new SanitizationReport();

        if (config.Sanitize.Count == 0)
        {
            logger.LogInformation("No sanitize rules defined — skipping sanitization");
            return report;
        }

        await using var conn = new NpgsqlConnection(config.Migration.Target.ToConnectionString());
        await conn.OpenAsync(ct);
        logger.LogInformation("Connected to target DB for sanitization");

        // All rules run inside a single transaction: all-or-nothing semantics
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            foreach (var rule in config.Sanitize)
            {
                var rowsAffected = await ApplyRuleAsync(conn, tx, rule, dryRun, ct);
                report.AddResult(rule.Table, rowsAffected);
            }

            if (!dryRun)
            {
                await tx.CommitAsync(ct);
                logger.LogInformation("Sanitization transaction committed");
            }
            else
            {
                await tx.RollbackAsync(ct);
                logger.LogInformation("[DRY RUN] Transaction rolled back — no changes applied");
            }
        }
        catch
        {
            await tx.RollbackAsync(ct);
            logger.LogError("Sanitization failed — transaction rolled back");
            throw;
        }

        return report;
    }

    private async Task<long> ApplyRuleAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        SanitizeRule rule,
        bool dryRun,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildUpdateSql(rule);

        if (dryRun)
        {
            logger.LogInformation("[DRY RUN] Would execute: {Sql}", sql);
            return 0;
        }

        logger.LogDebug("Executing: {Sql}", sql);

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        foreach (var (key, value) in parameters)
            cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Sanitized table '{Table}': {Rows} row(s) updated", rule.Table, rowsAffected);

        return rowsAffected;
    }

    /// <summary>
    /// Builds the parameterized UPDATE SQL for a sanitize rule.
    /// Extracted as an internal method to enable unit testing without a DB connection.
    /// </summary>
    public static (string Sql, Dictionary<string, object?> Parameters) BuildUpdateSql(SanitizeRule rule)
    {
        var parameters = new Dictionary<string, object?>();
        var setClauses = new List<string>();

        foreach (var colRule in rule.Columns)
        {
            var strategy = StrategyFactory.Create(colRule);
            var paramName = $"p_{colRule.Name}";
            var sqlExpr = strategy.BuildSqlExpression(colRule, paramName, parameters);

            // Column identifier is already validated to be a safe identifier (only [a-zA-Z0-9_])
            // by ConfigLoader.ValidateStructure before reaching this point.
            var columnIdentifier = $"\"{colRule.Name}\"";
            setClauses.Add(sqlExpr is null
                ? $"{columnIdentifier} = NULL"
                : $"{columnIdentifier} = {sqlExpr}");
        }

        var sql = $"""UPDATE "{rule.Table}" SET {string.Join(", ", setClauses)}""";

        if (rule.Where is not null)
        {
            // WHERE clause is from a trusted config file (not user input).
            // ConfigLoader.ValidateStructure must be called before this point.
            sql += $" WHERE {rule.Where}";
        }

        return (sql, parameters);
    }
}

public class SanitizationReport
{
    private readonly List<TableResult> _results = [];

    public IReadOnlyList<TableResult> Results => _results;
    public long TotalRowsAffected => _results.Sum(r => r.RowsAffected);

    public void AddResult(string table, long rowsAffected) =>
        _results.Add(new TableResult(table, rowsAffected));
}

public record TableResult(string Table, long RowsAffected);
