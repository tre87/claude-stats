namespace ClaudeStats.Console.Data;

public sealed record InterceptedData(
    string UsageJson,
    string? OverageLimitJson,
    string? CreditGrantJson,
    string? PrepaidCreditsJson);