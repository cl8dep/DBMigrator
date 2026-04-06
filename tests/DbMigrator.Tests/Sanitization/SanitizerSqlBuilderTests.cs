using DbMigrator.Config;
using DbMigrator.Sanitization;
using FluentAssertions;

namespace DbMigrator.Tests.Sanitization;

/// <summary>
/// Tests for Sanitizer.BuildUpdateSql — the SQL generation logic
/// that runs without a database connection.
/// </summary>
public class SanitizerSqlBuilderTests
{
    [Fact]
    public void BuildUpdateSql_SingleStaticColumn_ProducesCorrectSql()
    {
        var rule = new SanitizeRule
        {
            Table = "users",
            Columns = [new ColumnRule { Name = "email", Strategy = "static", Value = "redacted@test.com" }]
        };

        var (sql, parameters) = Sanitizer.BuildUpdateSql(rule);

        sql.Should().Be("""UPDATE "users" SET "email" = @p_email""");
        parameters.Should().ContainKey("p_email");
        parameters["p_email"].Should().Be("redacted@test.com");
    }

    [Fact]
    public void BuildUpdateSql_NullValueStrategy_ProducesSqlNull()
    {
        var rule = new SanitizeRule
        {
            Table = "users",
            Columns = [new ColumnRule { Name = "avatar_url", Strategy = "null_value" }]
        };

        var result = Sanitizer.BuildUpdateSql(rule);

        result.Sql.Should().Contain("""= NULL""");
        result.Sql.Should().NotContain("@p_avatar_url");
    }

    [Fact]
    public void BuildUpdateSql_MultipleColumns_JoinedWithComma()
    {
        var rule = new SanitizeRule
        {
            Table = "users",
            Columns =
            [
                new ColumnRule { Name = "email", Strategy = "static", Value = "x@x.com" },
                new ColumnRule { Name = "phone", Strategy = "null_value" }
            ]
        };

        var result = Sanitizer.BuildUpdateSql(rule);

        result.Sql.Should().Contain(", ");
        result.Sql.Should().Contain(""""email"""");
        result.Sql.Should().Contain(""""phone"""");
    }

    [Fact]
    public void BuildUpdateSql_WithWhereClause_AppendsWhereToSql()
    {
        var rule = new SanitizeRule
        {
            Table = "users",
            Where = "role != 'admin'",
            Columns = [new ColumnRule { Name = "email", Strategy = "static", Value = "x@x.com" }]
        };

        var (sql, _) = Sanitizer.BuildUpdateSql(rule);

        sql.Should().EndWith("WHERE role != 'admin'");
    }

    [Fact]
    public void BuildUpdateSql_WithoutWhereClause_NoWhereInSql()
    {
        var rule = new SanitizeRule
        {
            Table = "users",
            Where = null,
            Columns = [new ColumnRule { Name = "email", Strategy = "static", Value = "x" }]
        };

        var result = Sanitizer.BuildUpdateSql(rule);

        result.Sql.Should().NotContain("WHERE");
    }

    [Fact]
    public void BuildUpdateSql_HashStrategy_ProducesPureSqlExpression_NoParams()
    {
        var rule = new SanitizeRule
        {
            Table = "users",
            Columns = [new ColumnRule { Name = "national_id", Strategy = "hash" }]
        };

        var (sql, parameters) = Sanitizer.BuildUpdateSql(rule);

        sql.Should().Contain("sha256");
        sql.Should().Contain(""""national_id"""");
        parameters.Should().BeEmpty("hash strategy uses a SQL expression with no parameters");
    }

    [Fact]
    public void BuildUpdateSql_TemplateStrategy_ProducesConcatenationSql()
    {
        var rule = new SanitizeRule
        {
            Table = "users",
            Columns = [new ColumnRule { Name = "email", Strategy = "template", Value = "user_{id}@test.local" }]
        };

        var result = Sanitizer.BuildUpdateSql(rule);

        result.Sql.Should().Contain("||");
        result.Sql.Should().Contain("\"id\"::text");
    }

    [Fact]
    public void BuildUpdateSql_FakerStrategy_AddsParameterWithNonNullValue()
    {
        var rule = new SanitizeRule
        {
            Table = "users",
            Columns = [new ColumnRule { Name = "first_name", Strategy = "faker", Faker = "first_name" }]
        };

        var (sql, parameters) = Sanitizer.BuildUpdateSql(rule);

        sql.Should().Contain("@p_first_name");
        parameters.Should().ContainKey("p_first_name");
        parameters["p_first_name"].Should().NotBeNull();
        parameters["p_first_name"].Should().BeOfType<string>();
    }

    [Fact]
    public void BuildUpdateSql_TableNameIsQuoted()
    {
        var rule = new SanitizeRule
        {
            Table = "my_table",
            Columns = [new ColumnRule { Name = "col", Strategy = "null_value" }]
        };

        var (sql, _) = Sanitizer.BuildUpdateSql(rule);

        sql.Should().StartWith("""UPDATE "my_table" SET""");
    }

    [Fact]
    public void BuildUpdateSql_MixedStrategies_CorrectParameterCount()
    {
        var rule = new SanitizeRule
        {
            Table = "users",
            Columns =
            [
                new ColumnRule { Name = "email", Strategy = "static", Value = "x@x.com" },   // 1 param
                new ColumnRule { Name = "phone", Strategy = "null_value" },                    // 0 params
                new ColumnRule { Name = "name", Strategy = "faker", Faker = "full_name" },    // 1 param
                new ColumnRule { Name = "id_hash", Strategy = "hash" }                        // 0 params (SQL expr)
            ]
        };

        var (_, parameters) = Sanitizer.BuildUpdateSql(rule);

        parameters.Should().HaveCount(2, "static and faker each add 1 parameter; null_value and hash add none");
        parameters.Should().ContainKey("p_email");
        parameters.Should().ContainKey("p_name");
    }

    [Fact]
    public void SanitizationReport_TracksResultsAndTotals()
    {
        var report = new SanitizationReport();
        report.AddResult("users", 50);
        report.AddResult("orders", 30);

        report.Results.Should().HaveCount(2);
        report.TotalRowsAffected.Should().Be(80);
    }

    [Fact]
    public void SanitizationReport_EmptyReport_TotalIsZero()
    {
        var report = new SanitizationReport();
        report.TotalRowsAffected.Should().Be(0);
        report.Results.Should().BeEmpty();
    }
}
