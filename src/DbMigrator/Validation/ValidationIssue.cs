namespace DbMigrator.Validation;

public enum Severity { Error, Warning }

public record ValidationIssue(Severity Severity, string Message)
{
    public override string ToString() =>
        $"[{Severity.ToString().ToUpper()}] {Message}";
}
