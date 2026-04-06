namespace DbMigrator.Validation;

public class ValidationResult
{
    private readonly List<ValidationIssue> _issues = [];

    public IReadOnlyList<ValidationIssue> Issues => _issues;

    public IEnumerable<ValidationIssue> Errors =>
        _issues.Where(i => i.Severity == Severity.Error);

    public IEnumerable<ValidationIssue> Warnings =>
        _issues.Where(i => i.Severity == Severity.Warning);

    public bool HasErrors => _issues.Any(i => i.Severity == Severity.Error);

    public void AddError(string message) =>
        _issues.Add(new ValidationIssue(Severity.Error, message));

    public void AddWarning(string message) =>
        _issues.Add(new ValidationIssue(Severity.Warning, message));

    public void Merge(ValidationResult other) =>
        _issues.AddRange(other._issues);
}
