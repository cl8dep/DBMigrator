using DbMigrator.Config;
using DbMigrator.Sanitization.Strategies;
using FluentAssertions;

namespace DbMigrator.Tests.Sanitization;

public class StaticStrategyTests
{
    [Fact]
    public void BuildSqlExpression_ReturnsParamAndAddsValue()
    {
        var strategy = new StaticStrategy();
        var rule = new ColumnRule { Name = "email", Strategy = "static", Value = "redacted@test.com" };
        var parameters = new Dictionary<string, object?>();

        var expr = strategy.BuildSqlExpression(rule, "p_email", parameters);

        expr.Should().Be("@p_email");
        parameters.Should().ContainKey("p_email");
        parameters["p_email"].Should().Be("redacted@test.com");
    }

    [Fact]
    public void BuildSqlExpression_AcceptsNullValue()
    {
        var strategy = new StaticStrategy();
        var rule = new ColumnRule { Name = "col", Strategy = "static", Value = null };
        var parameters = new Dictionary<string, object?>();

        var expr = strategy.BuildSqlExpression(rule, "p_col", parameters);

        expr.Should().Be("@p_col");
        parameters["p_col"].Should().BeNull();
    }
}

public class NullStrategyTests
{
    [Fact]
    public void BuildSqlExpression_ReturnsNull()
    {
        var strategy = new NullStrategy();
        var rule = new ColumnRule { Name = "col", Strategy = "null_value" };
        var parameters = new Dictionary<string, object?>();

        var expr = strategy.BuildSqlExpression(rule, "p_col", parameters);

        expr.Should().BeNull();
        parameters.Should().BeEmpty();
    }
}

public class HashStrategyTests
{
    [Fact]
    public void BuildSqlExpression_ReturnsSqlExpression_NoParams()
    {
        var strategy = new HashStrategy();
        var rule = new ColumnRule { Name = "email", Strategy = "hash" };
        var parameters = new Dictionary<string, object?>();

        var expr = strategy.BuildSqlExpression(rule, "p_email", parameters);

        expr.Should().Contain("sha256");
        expr.Should().Contain("\"email\"");
        parameters.Should().BeEmpty("hash uses a pure SQL expression with no parameters");
    }

    [Fact]
    public void HashValue_ProducesDeterministicHex()
    {
        var h1 = HashStrategy.HashValue("hello");
        var h2 = HashStrategy.HashValue("hello");
        var h3 = HashStrategy.HashValue("world");

        h1.Should().Be(h2);
        h1.Should().NotBe(h3);
        h1.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void HashValue_MatchesKnownSha256()
    {
        // SHA256("hello") = 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
        var result = HashStrategy.HashValue("hello");
        result.Should().Be("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
    }
}

public class TemplateStrategyTests
{
    [Fact]
    public void ParseTemplate_PlainString_ReturnsSingleLiteral()
    {
        var parts = TemplateStrategy.ParseTemplate("hello@example.com");
        parts.Should().HaveCount(1);
        parts[0].Text.Should().Be("hello@example.com");
        parts[0].IsColumnRef.Should().BeFalse();
    }

    [Fact]
    public void ParseTemplate_WithColumnRef_SplitsCorrectly()
    {
        var parts = TemplateStrategy.ParseTemplate("user_{id}@redacted.local");

        parts.Should().HaveCount(3);
        parts[0].Should().Be(new TemplateStrategy.TemplatePart("user_", false));
        parts[1].Should().Be(new TemplateStrategy.TemplatePart("id", true));
        parts[2].Should().Be(new TemplateStrategy.TemplatePart("@redacted.local", false));
    }

    [Fact]
    public void ParseTemplate_MultipleRefs_ParsesAll()
    {
        var parts = TemplateStrategy.ParseTemplate("{first}_{last}@corp.com");

        parts.Should().HaveCount(4);
        parts[0].Should().Be(new TemplateStrategy.TemplatePart("first", true));
        parts[1].Should().Be(new TemplateStrategy.TemplatePart("_", false));
        parts[2].Should().Be(new TemplateStrategy.TemplatePart("last", true));
        parts[3].Should().Be(new TemplateStrategy.TemplatePart("@corp.com", false));
    }

    [Fact]
    public void BuildSqlExpression_ProducesCorrectConcatenation()
    {
        var strategy = new TemplateStrategy();
        var rule = new ColumnRule { Name = "email", Strategy = "template", Value = "user_{id}@test.local" };
        var parameters = new Dictionary<string, object?>();

        var expr = strategy.BuildSqlExpression(rule, "p_email", parameters);

        expr.Should().NotBeNull();
        expr.Should().Contain("\"id\"::text");
        expr.Should().Contain(" || ");
    }

    [Fact]
    public void BuildSqlExpression_ThrowsWhenValueMissing()
    {
        var strategy = new TemplateStrategy();
        var rule = new ColumnRule { Name = "email", Strategy = "template", Value = null };
        var parameters = new Dictionary<string, object?>();

        var act = () => strategy.BuildSqlExpression(rule, "p_email", parameters);
        act.Should().Throw<InvalidOperationException>().WithMessage("*template*");
    }
}

public class FakerStrategyTests
{
    [Fact]
    public void BuildSqlExpression_KnownMethod_ReturnsNonNullValue()
    {
        var strategy = new FakerStrategy();
        var rule = new ColumnRule { Name = "email", Strategy = "faker", Faker = "email" };
        var parameters = new Dictionary<string, object?>();

        var expr = strategy.BuildSqlExpression(rule, "p_email", parameters);

        expr.Should().Be("@p_email");
        parameters.Should().ContainKey("p_email");
        parameters["p_email"].Should().BeOfType<string>()
            .Which.Should().Contain("@");
    }

    [Fact]
    public void BuildSqlExpression_UnknownMethod_ThrowsDescriptiveError()
    {
        var strategy = new FakerStrategy();
        var rule = new ColumnRule { Name = "col", Strategy = "faker", Faker = "nonexistent_method" };
        var parameters = new Dictionary<string, object?>();

        var act = () => strategy.BuildSqlExpression(rule, "p_col", parameters);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*nonexistent_method*");
    }

    [Fact]
    public void BuildSqlExpression_ThrowsWhenFakerFieldMissing()
    {
        var strategy = new FakerStrategy();
        var rule = new ColumnRule { Name = "col", Strategy = "faker", Faker = null };
        var parameters = new Dictionary<string, object?>();

        var act = () => strategy.BuildSqlExpression(rule, "p_col", parameters);
        act.Should().Throw<InvalidOperationException>().WithMessage("*faker*");
    }

    [Theory]
    [InlineData("first_name")]
    [InlineData("last_name")]
    [InlineData("email")]
    [InlineData("phone")]
    [InlineData("address")]
    [InlineData("company")]
    [InlineData("uuid")]
    public void BuildSqlExpression_AllCommonMethods_ProduceNonNullValues(string method)
    {
        var strategy = new FakerStrategy();
        var rule = new ColumnRule { Name = "col", Strategy = "faker", Faker = method };
        var parameters = new Dictionary<string, object?>();

        var expr = strategy.BuildSqlExpression(rule, "p_col", parameters);

        expr.Should().Be("@p_col");
        parameters["p_col"].Should().NotBeNull();
    }
}

public class StrategyFactoryTests
{
    [Theory]
    [InlineData("static", typeof(StaticStrategy))]
    [InlineData("null_value", typeof(NullStrategy))]
    [InlineData("template", typeof(TemplateStrategy))]
    [InlineData("faker", typeof(FakerStrategy))]
    [InlineData("hash", typeof(HashStrategy))]
    public void Create_ReturnsCorrectStrategy(string strategyName, Type expectedType)
    {
        var rule = new ColumnRule { Name = "col", Strategy = strategyName };
        var strategy = StrategyFactory.Create(rule);
        strategy.Should().BeOfType(expectedType);
    }

    [Fact]
    public void Create_UnknownStrategy_ThrowsArgumentException()
    {
        var rule = new ColumnRule { Name = "col", Strategy = "magic" };
        var act = () => StrategyFactory.Create(rule);
        act.Should().Throw<ArgumentException>().WithMessage("*magic*");
    }

    [Fact]
    public void Create_IsCaseInsensitive()
    {
        var rule = new ColumnRule { Name = "col", Strategy = "STATIC" };
        var strategy = StrategyFactory.Create(rule);
        strategy.Should().BeOfType<StaticStrategy>();
    }
}
