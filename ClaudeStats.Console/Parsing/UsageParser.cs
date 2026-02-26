using System.Text.Json;
using ClaudeStats.Console.Data;
using ClaudeStats.Console.Models;

namespace ClaudeStats.Console.Parsing;

/// <summary>
///     Parses the three intercepted API responses into a single UsageData model.
///     /usage response shape:
///     {
///     "five_hour":  { "utilization": 100.0, "resets_at": "..." },
///     "seven_day":  { "utilization": 25.0,  "resets_at": "..." },
///     "seven_day_opus":    null,
///     "seven_day_sonnet":  null,
///     "extra_usage":       null   ← or { "utilization": X, "resets_at": "..." }
///     }
///     /overage_spend_limit response shape:
///     {
///     "is_enabled": true,
///     "monthly_credit_limit": 2000,      ← minor units (cents)
///     "used_credits": 1352,              ← minor units spent so far (field name varies: also "current_spend")
///     "currency": "EUR",
///     ...
///     }
///     /overage_credit_grant response shape:
///     {
///     "available": false,
///     "eligible": false,
///     "granted": false,
///     "amount_minor_units": null,
///     "currency": null
///     }
/// </summary>
public static class UsageParser
{
    public static UsageData? Parse(InterceptedData intercepted)
    {
        var data = ParseUsageJson(intercepted.UsageJson);
        if (data is null)
        {
            return null;
        }

        if (intercepted.OverageLimitJson is not null)
        {
            data.OverageSpendLimit = ParseOverageSpendLimit(intercepted.OverageLimitJson);
        }

        if (intercepted.CreditGrantJson is not null)
        {
            data.OverageCreditGrant = ParseCreditGrant(intercepted.CreditGrantJson);
        }

        if (intercepted.PrepaidCreditsJson is not null)
        {
            data.PrepaidCredits = ParsePrepaidCredits(intercepted.PrepaidCreditsJson);
        }

        data.Plan = DerivePlan(data);

        return data;
    }

    private static PlanTier DerivePlan(UsageData data)
    {
        // Max: has model-specific sub-limits (Opus or Sonnet buckets) — check before Team
        if (data.SevenDayOpus is not null || data.SevenDaySonnet is not null)
        {
            return PlanTier.Max;
        }

        // Team: seat_tier is set (non-null) — this only appears on actual Team/Business accounts.
        // Pro users with pay-per-use extra credits have is_enabled=true but seat_tier=null.
        if (data.OverageSpendLimit?.SeatTier is not null)
        {
            return PlanTier.Team;
        }

        // Free: only has the 5-hour window, no 7-day
        if (data.SevenDay is null && data.FiveHour is not null)
        {
            return PlanTier.Free;
        }

        // Pro: has 7-day window
        if (data.SevenDay is not null)
        {
            return PlanTier.Pro;
        }

        return PlanTier.Unknown;
    }

    private static UsageData? ParseUsageJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("five_hour", out _) && !root.TryGetProperty("seven_day", out _))
            {
                return null;
            }

            var data = new UsageData { RawJson = json };
            data.FiveHour = ParsePeriod("Current Session (5h)", root, "five_hour");
            data.SevenDay = ParsePeriod("Weekly (7d)", root, "seven_day");
            data.SevenDayOpus = ParsePeriod("Opus (7d)", root, "seven_day_opus");
            data.SevenDaySonnet = ParsePeriod("Sonnet (7d)", root, "seven_day_sonnet");
            data.ExtraUsage = ParseExtraUsagePeriod(root);
            return data;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UsagePeriod? ParsePeriod(string label, JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!el.TryGetProperty("utilization", out var uEl) || !uEl.TryGetDouble(out var utilization))
        {
            return null;
        }

        DateTimeOffset? resetsAt = null;
        if (el.TryGetProperty("resets_at", out var rEl) && rEl.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(rEl.GetString(), out var parsed))
        {
            resetsAt = parsed;
        }

        return new UsagePeriod
        {
            Label = label,
            Utilization = Math.Clamp(utilization, 0, 100),
            ResetsAt = resetsAt
        };
    }

    private static ExtraUsagePeriod? ParseExtraUsagePeriod(JsonElement root)
    {
        if (!root.TryGetProperty("extra_usage", out var el) || el.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        double? utilization = null;
        if (el.TryGetProperty("utilization", out var uEl) && uEl.TryGetDouble(out var u))
        {
            utilization = u;
        }

        DateTimeOffset? resetsAt = null;
        if (el.TryGetProperty("resets_at", out var rEl) && rEl.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(rEl.GetString(), out var parsed))
        {
            resetsAt = parsed;
        }

        return new ExtraUsagePeriod { Utilization = utilization, ResetsAt = resetsAt };
    }

    private static OverageSpendLimit? ParseOverageSpendLimit(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var isEnabled = root.TryGetProperty("is_enabled", out var isEnabledEl) &&
                            isEnabledEl.ValueKind == JsonValueKind.True;

            long? monthlyLimit = null;
            if (root.TryGetProperty("monthly_credit_limit", out var limitEl) &&
                limitEl.ValueKind == JsonValueKind.Number && limitEl.TryGetInt64(out var lv))
            {
                monthlyLimit = lv;
            }

            long? currentSpend = null;
            // Field may be "current_spend" or "used_credits" depending on the response variant
            if (root.TryGetProperty("used_credits", out var spendEl) &&
                spendEl.ValueKind == JsonValueKind.Number && spendEl.TryGetInt64(out var sv))
            {
                currentSpend = sv;
            }
            else if (root.TryGetProperty("current_spend", out var spendEl2) &&
                     spendEl2.ValueKind == JsonValueKind.Number && spendEl2.TryGetInt64(out var sv2))
            {
                currentSpend = sv2;
            }

            string? currency = null;
            if (root.TryGetProperty("currency", out var currEl) && currEl.ValueKind == JsonValueKind.String)
            {
                currency = currEl.GetString();
            }

            string? seatTier = null;
            if (root.TryGetProperty("seat_tier", out var stEl) && stEl.ValueKind == JsonValueKind.String)
            {
                seatTier = stEl.GetString();
            }

            return new OverageSpendLimit
            {
                IsEnabled = isEnabled,
                MonthlyCreditLimit = monthlyLimit,
                CurrentSpend = currentSpend,
                Currency = currency,
                SeatTier = seatTier
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PrepaidCredits? ParsePrepaidCredits(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            long? amount = null;
            if (root.TryGetProperty("amount", out var amtEl) &&
                amtEl.ValueKind == JsonValueKind.Number && amtEl.TryGetInt64(out var amtV))
            {
                amount = amtV;
            }

            string? currency = null;
            if (root.TryGetProperty("currency", out var currEl) && currEl.ValueKind == JsonValueKind.String)
            {
                currency = currEl.GetString();
            }

            return new PrepaidCredits { Amount = amount, Currency = currency };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static OverageCreditGrant? ParseCreditGrant(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var available = root.TryGetProperty("available", out var aEl) && aEl.ValueKind == JsonValueKind.True;
            var eligible = root.TryGetProperty("eligible", out var eEl) && eEl.ValueKind == JsonValueKind.True;
            var granted = root.TryGetProperty("granted", out var gEl) && gEl.ValueKind == JsonValueKind.True;

            long? amount = null;
            if (root.TryGetProperty("amount_minor_units", out var amtEl) &&
                amtEl.ValueKind == JsonValueKind.Number && amtEl.TryGetInt64(out var amtV))
            {
                amount = amtV;
            }

            string? currency = null;
            if (root.TryGetProperty("currency", out var currEl) && currEl.ValueKind == JsonValueKind.String)
            {
                currency = currEl.GetString();
            }

            return new OverageCreditGrant
            {
                Available = available,
                Eligible = eligible,
                Granted = granted,
                AmountMinorUnits = amount,
                Currency = currency
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}