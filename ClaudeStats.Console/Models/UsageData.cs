namespace ClaudeStats.Console.Models;

public enum PlanTier
{
    Unknown,
    Free,
    Pro,
    Max,
    Team
}

/// <summary>
///     Full usage picture assembled from three claude.ai API endpoints.
/// </summary>
public sealed class UsageData
{
    public PlanTier Plan { get; set; } = PlanTier.Unknown;
    /// <summary>5-hour rolling session window. From /usage.</summary>
    public UsagePeriod? FiveHour { get; set; }

    /// <summary>7-day rolling window. From /usage.</summary>
    public UsagePeriod? SevenDay { get; set; }

    /// <summary>7-day Opus-specific sub-limit, if applicable. From /usage.</summary>
    public UsagePeriod? SevenDayOpus { get; set; }

    /// <summary>7-day Sonnet-specific sub-limit, if applicable. From /usage.</summary>
    public UsagePeriod? SevenDaySonnet { get; set; }

    /// <summary>Extra/overage usage bucket inside the /usage response (rarely populated).</summary>
    public ExtraUsagePeriod? ExtraUsage { get; set; }

    /// <summary>
    ///     Pay-per-use overage spend limit. From /overage_spend_limit.
    ///     Shows how much of the monthly credit limit has been consumed.
    /// </summary>
    public OverageSpendLimit? OverageSpendLimit { get; set; }

    /// <summary>
    ///     One-time overage credit grant. From /overage_credit_grant.
    /// </summary>
    public OverageCreditGrant? OverageCreditGrant { get; set; }

    /// <summary>
    ///     Prepaid credit balance. From /prepaid/credits.
    /// </summary>
    public PrepaidCredits? PrepaidCredits { get; set; }

    public string? RawJson { get; set; }
}

public sealed class UsagePeriod
{
    public string Label { get; set; } = string.Empty;

    /// <summary>Utilization as a value between 0 and 100.</summary>
    public double Utilization { get; set; }

    public DateTimeOffset? ResetsAt { get; set; }

    public TimeSpan? TimeUntilReset =>
        ResetsAt.HasValue ? ResetsAt.Value - DateTimeOffset.UtcNow : null;

    public bool IsAtLimit => Utilization >= 100.0;
}

/// <summary>The extra_usage object inside the /usage response (when non-null).</summary>
public sealed class ExtraUsagePeriod
{
    public double? Utilization { get; set; }
    public DateTimeOffset? ResetsAt { get; set; }
}

/// <summary>
///     From GET /api/organizations/{id}/overage_spend_limit.
///     Amounts are in the currency's minor units (e.g. EUR cents).
/// </summary>
public sealed class OverageSpendLimit
{
    public bool IsEnabled { get; set; }
    public string? Currency { get; set; }

    /// <summary>Non-null on Team plans (e.g. "pro", "team"). Null on Pro+pay-per-use.</summary>
    public string? SeatTier { get; set; }

    /// <summary>Monthly credit limit in minor units (e.g. 2000 = €20.00).</summary>
    public long? MonthlyCreditLimit { get; set; }

    /// <summary>Amount spent so far this month in minor units (from "used_credits" or "current_spend" field).</summary>
    public long? CurrentSpend { get; set; }

    public double? SpendPercent =>
        MonthlyCreditLimit is > 0 && CurrentSpend.HasValue
            ? Math.Min(100.0, (double)CurrentSpend.Value / MonthlyCreditLimit.Value * 100.0)
            : null;

    public long? Remaining =>
        MonthlyCreditLimit.HasValue && CurrentSpend.HasValue
            ? Math.Max(0, MonthlyCreditLimit.Value - CurrentSpend.Value)
            : null;

    /// <summary>Formats a minor-unit amount as a currency string, e.g. €8.66.</summary>
    public string FormatAmount(long? minorUnits)
    {
        if (!minorUnits.HasValue)
        {
            return "?";
        }

        var symbol = Currency?.ToUpperInvariant() switch
        {
            "EUR" => "€",
            "USD" => "$",
            "GBP" => "£",
            _ => Currency ?? ""
        };
        return $"{symbol}{minorUnits.Value / 100.0:F2}";
    }
}

/// <summary>From GET /api/organizations/{id}/prepaid/credits.</summary>
public sealed class PrepaidCredits
{
    /// <summary>Current prepaid balance in minor units (e.g. 473 = €4.73).</summary>
    public long? Amount { get; set; }

    public string? Currency { get; set; }

    public string FormatAmount()
    {
        if (!Amount.HasValue)
        {
            return "?";
        }

        var symbol = Currency?.ToUpperInvariant() switch
        {
            "EUR" => "€",
            "USD" => "$",
            "GBP" => "£",
            _ => Currency ?? ""
        };
        return $"{symbol}{Amount.Value / 100.0:F2}";
    }
}

/// <summary>From GET /api/organizations/{id}/overage_credit_grant.</summary>
public sealed class OverageCreditGrant
{
    public bool Available { get; set; }
    public bool Eligible { get; set; }
    public bool Granted { get; set; }

    /// <summary>Grant amount in minor units, if granted.</summary>
    public long? AmountMinorUnits { get; set; }

    public string? Currency { get; set; }

    public string FormatAmount()
    {
        if (!AmountMinorUnits.HasValue)
        {
            return "?";
        }

        var symbol = Currency?.ToUpperInvariant() switch
        {
            "EUR" => "€",
            "USD" => "$",
            "GBP" => "£",
            _ => Currency ?? ""
        };
        return $"{symbol}{AmountMinorUnits.Value / 100.0:F2}";
    }
}