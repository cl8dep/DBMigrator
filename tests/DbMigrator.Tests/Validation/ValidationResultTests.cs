using DbMigrator.Validation;
using FluentAssertions;

namespace DbMigrator.Tests.Validation;

public class ValidationResultTests
{
    [Fact]
    public void HasErrors_FalseWhenEmpty()
    {
        var result = new ValidationResult();
        result.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void HasErrors_TrueWhenErrorAdded()
    {
        var result = new ValidationResult();
        result.AddError("something went wrong");
        result.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void HasErrors_FalseWhenOnlyWarnings()
    {
        var result = new ValidationResult();
        result.AddWarning("watch out");
        result.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Errors_ReturnsOnlyErrors()
    {
        var result = new ValidationResult();
        result.AddError("err1");
        result.AddWarning("warn1");
        result.AddError("err2");

        result.Errors.Should().HaveCount(2);
        result.Warnings.Should().HaveCount(1);
    }

    [Fact]
    public void Merge_CombinesIssues()
    {
        var r1 = new ValidationResult();
        r1.AddError("err from r1");

        var r2 = new ValidationResult();
        r2.AddWarning("warn from r2");

        r1.Merge(r2);

        r1.Issues.Should().HaveCount(2);
        r1.Errors.Should().HaveCount(1);
        r1.Warnings.Should().HaveCount(1);
    }

    [Fact]
    public void ValidationIssue_ToString_IncludesSeverityAndMessage()
    {
        var issue = new ValidationIssue(Severity.Error, "table not found");
        issue.ToString().Should().Contain("ERROR");
        issue.ToString().Should().Contain("table not found");
    }
}
