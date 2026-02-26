using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace ClaudeStats.Console.Data;

/// <summary>
/// Intercepts the four claude.ai API responses needed to build the full usage picture:
///   1. /api/organizations/{id}/usage                — 5h/7d utilization percentages
///   2. /api/organizations/{id}/overage_spend_limit  — extra usage credit limit + spend
///   3. /api/organizations/{id}/overage_credit_grant — one-time credit grant status
///   4. /api/organizations/{id}/prepaid/credits      — current prepaid balance
/// </summary>
public sealed class NetworkInterceptor
{
    private readonly bool _discoverMode;
    private readonly List<(string Url, string ContentType, string Preview)> _discovered = [];

    // One TCS per expected endpoint; all resolve independently
    private readonly TaskCompletionSource<string> _usageTcs          = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _overageLimitTcs   = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _creditGrantTcs    = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _prepaidCreditsTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Compiled regexes for the four endpoints
    private static readonly Regex UsageEndpoint          = new(@"/api/organizations/[0-9a-f\-]+/usage$",                 RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OverageLimitEndpoint   = new(@"/api/organizations/[0-9a-f\-]+/overage_spend_limit$",   RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreditGrantEndpoint    = new(@"/api/organizations/[0-9a-f\-]+/overage_credit_grant$",  RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PrepaidCreditsEndpoint = new(@"/api/organizations/[0-9a-f\-]+/prepaid/credits$",       RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public NetworkInterceptor(bool discoverMode = false)
    {
        _discoverMode = discoverMode;
    }

    public IReadOnlyList<(string Url, string ContentType, string Preview)> DiscoveredResponses
        => _discovered.AsReadOnly();

    public void Register(IPage page)
    {
        page.Response += async (_, response) =>
        {
            try
            {
                var url = response.Url;

                if (response.Status != 200)
                    return;

                if (!url.Contains("claude.ai"))
                    return;

                // Skip static assets
                if (url.EndsWith(".js") || url.EndsWith(".css") || url.EndsWith(".ico") ||
                    url.EndsWith(".png") || url.EndsWith(".svg") || url.EndsWith(".woff2"))
                    return;

                var contentType = response.Headers.GetValueOrDefault("content-type", "");
                if (!contentType.Contains("json"))
                    return;

                string body;
                try { body = await response.TextAsync(); }
                catch { return; }

                if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith("{"))
                    return;

                if (_discoverMode)
                {
                    var preview = body.Length > 500 ? body[..500] + "..." : body;
                    _discovered.Add((url, contentType, preview));
                }

                // Route to the appropriate TCS
                if (UsageEndpoint.IsMatch(url) &&
                    (body.Contains("\"five_hour\"") || body.Contains("\"seven_day\"")))
                {
                    _usageTcs.TrySetResult(body);
                }
                else if (OverageLimitEndpoint.IsMatch(url))
                {
                    _overageLimitTcs.TrySetResult(body);
                }
                else if (CreditGrantEndpoint.IsMatch(url))
                {
                    _creditGrantTcs.TrySetResult(body);
                }
                else if (PrepaidCreditsEndpoint.IsMatch(url))
                {
                    _prepaidCreditsTcs.TrySetResult(body);
                }
            }
            catch
            {
                // Ignore errors from individual responses
            }
        };
    }

    /// <summary>
    /// Waits for the primary usage response (required).
    /// The overage responses are optional and resolved on a best-effort basis.
    /// </summary>
    public async Task<InterceptedData> WaitForDataAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        // The usage endpoint is required
        string usageJson;
        try
        {
            usageJson = await _usageTcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Timed out waiting for usage data from claude.ai.");
        }

        // Give overage endpoints a short grace period to arrive (they load in parallel)
        using var graceCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        string? overageLimitJson = null;
        string? creditGrantJson  = null;

        string? prepaidCreditsJson = null;

        try { overageLimitJson   = await _overageLimitTcs.Task.WaitAsync(graceCts.Token);   } catch { /* optional */ }
        try { creditGrantJson    = await _creditGrantTcs.Task.WaitAsync(graceCts.Token);    } catch { /* optional */ }
        try { prepaidCreditsJson = await _prepaidCreditsTcs.Task.WaitAsync(graceCts.Token); } catch { /* optional */ }

        return new InterceptedData(usageJson, overageLimitJson, creditGrantJson, prepaidCreditsJson);
    }
}

public sealed record InterceptedData(
    string UsageJson,
    string? OverageLimitJson,
    string? CreditGrantJson,
    string? PrepaidCreditsJson);
