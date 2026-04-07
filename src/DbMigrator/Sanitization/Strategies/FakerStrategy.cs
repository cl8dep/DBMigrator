using Bogus;
using Bogus.Extensions.UnitedStates;
using DbMigrator.Config;

namespace DbMigrator.Sanitization.Strategies;

/// <summary>
/// Generates a realistic fake value per-row using Bogus.
/// The 'faker' field in config maps to a Bogus dataset method.
/// </summary>
public class FakerStrategy : IStrategy
{
    private readonly Faker _faker;

    /// <param name="seed">
    /// Optional seed for deterministic output. When provided, every run produces the same
    /// fake values in the same column-processing order. Omit for random output each run.
    /// </param>
    public FakerStrategy(int? seed = null)
    {
        _faker = new Faker("en");
        if (seed.HasValue)
            _faker.Random = new Bogus.Randomizer(seed.Value);
    }

    public static readonly IReadOnlySet<string> KnownMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "first_name", "last_name", "full_name", "email", "user_name", "phone",
        "address", "street_address", "city", "country", "zip_code", "company",
        "lorem", "paragraph", "url", "ip_address", "uuid", "random_number",
        "random_double", "digits_as_integer", "date", "datetime", "color",
        "product", "price", "iban", "credit_card", "ssn",
    };

    // Map of faker keys → functions that produce a fake value
    private readonly Dictionary<string, Func<Faker, object?>> _methods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["first_name"]       = f => f.Name.FirstName(),
        ["last_name"]        = f => f.Name.LastName(),
        ["full_name"]        = f => f.Name.FullName(),
        ["email"]            = f => f.Internet.Email(),
        ["user_name"]        = f => f.Internet.UserName(),
        ["phone"]            = f => f.Phone.PhoneNumber(),
        ["address"]          = f => f.Address.FullAddress(),
        ["street_address"]   = f => f.Address.StreetAddress(),
        ["city"]             = f => f.Address.City(),
        ["country"]          = f => f.Address.Country(),
        ["zip_code"]         = f => f.Address.ZipCode(),
        ["company"]          = f => f.Company.CompanyName(),
        ["lorem"]            = f => f.Lorem.Sentence(),
        ["paragraph"]        = f => f.Lorem.Paragraph(),
        ["url"]              = f => f.Internet.Url(),
        ["ip_address"]       = f => f.Internet.Ip(),
        ["uuid"]             = f => f.Random.Uuid().ToString(),
        ["random_number"]    = f => f.Random.Long(1, long.MaxValue),
        ["random_double"]    = f => f.Random.Double(),
        ["digits_as_integer"]= f => (long)f.Random.Int(1000, 9999),
        ["date"]             = f => f.Date.Past().ToString("yyyy-MM-dd"),
        ["datetime"]         = f => f.Date.Past(),
        ["color"]            = f => f.Commerce.Color(),
        ["product"]          = f => f.Commerce.ProductName(),
        ["price"]            = f => f.Commerce.Price(),
        ["iban"]             = f => f.Finance.Iban(),
        ["credit_card"]      = f => f.Finance.CreditCardNumber(),
        ["ssn"]              = f => f.Person.Ssn(),
    };

    public string? BuildSqlExpression(ColumnRule rule, string paramName, IDictionary<string, object?> parameters)
    {
        if (string.IsNullOrEmpty(rule.Faker))
            throw new InvalidOperationException(
                $"Strategy 'faker' on column '{rule.Name}' requires a 'faker' field.");

        if (!_methods.TryGetValue(rule.Faker, out var generator))
            throw new InvalidOperationException(
                $"Unknown faker method '{rule.Faker}' for column '{rule.Name}'. " +
                $"Available: {string.Join(", ", _methods.Keys.Order())}");

        parameters[paramName] = generator(_faker);
        return $"@{paramName}";
    }

    public IReadOnlyCollection<string> AvailableMethods => _methods.Keys;
}
