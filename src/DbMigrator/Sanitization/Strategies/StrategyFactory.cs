using DbMigrator.Config;

namespace DbMigrator.Sanitization.Strategies;

public static class StrategyFactory
{
    public static IStrategy Create(ColumnRule rule) =>
        rule.Strategy.ToLowerInvariant() switch
        {
            "static"       => new StaticStrategy(),
            "null_value"   => new NullStrategy(),
            "template"     => new TemplateStrategy(),
            "faker"        => new FakerStrategy(),
            "hash"         => new HashStrategy(),
            "partial_mask" => new PartialMaskStrategy(),
            "random_from"  => new RandomFromStrategy(),
            _ => throw new ArgumentException(
                $"Unknown strategy '{rule.Strategy}'. Valid: static, null_value, template, faker, hash, partial_mask, random_from")
        };
}
