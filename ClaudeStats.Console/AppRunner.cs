using ClaudeStats.Console.Browser;
using ClaudeStats.Console.Data;
using ClaudeStats.Console.Display;
using ClaudeStats.Console.Models;
using ClaudeStats.Console.Parsing;
using Microsoft.Playwright;
using Spectre.Console;

namespace ClaudeStats.Console;

public sealed class AppRunner
{
    private const string UsageUrl = "https://claude.ai/settings/usage";

    public async Task RunAsync(
        bool discoverMode = false,
        int intervalSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        PlaywrightSetup.EnsureBrowsersInstalled();

        System.Console.Clear();

        // One-time auth check before entering the loop
        var authenticated = await EnsureAuthenticatedAsync();
        if (!authenticated)
        {
            ConsoleRenderer.RenderError("Authentication failed or was cancelled.");
            return;
        }

        // Reuse the same browser context and page across refreshes
        await using var browserService = new BrowserService();
        IPage page;
        try
        {
            page = await browserService.CreateAuthenticatedPageAsync();
        }
        catch (Exception ex)
        {
            ConsoleRenderer.RenderError($"Failed to launch browser: {ex.Message}");
            return;
        }

        // Main refresh loop — runs until Ctrl+C
        while (!cancellationToken.IsCancellationRequested)
        {
            var data = await FetchAndDisplayAsync(page, discoverMode, cancellationToken, intervalSeconds);

            if (discoverMode || data is null)
                break;  // discover mode and failed fetches are one-shot

            // Tick every second, re-rendering with live countdowns — no API call needed
            var nextFetchAt = DateTimeOffset.Now.AddSeconds(intervalSeconds);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var remaining = nextFetchAt - DateTimeOffset.Now;
                    if (remaining <= TimeSpan.Zero)
                        break;

                    ConsoleRenderer.Render(data, DateTimeOffset.Now, intervalSeconds, remaining);
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Checks for a valid cached session. If missing or expired, opens Firefox
    /// for interactive login. Returns true when authenticated.
    /// </summary>
    private static async Task<bool> EnsureAuthenticatedAsync()
    {
        if (ClaudeLoginHandler.HasCachedSession)
        {
            bool valid = false;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("grey"))
                .StartAsync("Checking session...", async _ =>
                {
                    valid = await ClaudeLoginHandler.TrySilentSessionCheckAsync();
                });

            if (valid)
                return true;

            AnsiConsole.MarkupLine("[yellow]Session expired.[/] Opening Firefox to log in again...");
            AnsiConsole.WriteLine();
        }
        else
        {
            AnsiConsole.MarkupLine("[bold]No saved session found.[/] Opening Firefox to log in...");
            AnsiConsole.WriteLine();
        }

        var sessionKey = await ClaudeLoginHandler.InteractiveLoginAsync();

        if (string.IsNullOrEmpty(sessionKey))
        {
            ConsoleRenderer.RenderError("Could not retrieve a session key after login.");
            return false;
        }

        AnsiConsole.MarkupLine("[green]Login successful![/] Session saved for future use.");
        AnsiConsole.WriteLine();
        return true;
    }

    private static async Task<UsageData?> FetchAndDisplayAsync(
        IPage page,
        bool discoverMode,
        CancellationToken cancellationToken,
        int intervalSeconds = 60)
    {
        var interceptor = new NetworkInterceptor(discoverMode);
        interceptor.Register(page);

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("dodgerblue1"))
            .StartAsync("Fetching usage data from claude.ai...", async _ =>
            {
                try
                {
                    await page.GotoAsync(UsageUrl, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.NetworkIdle,
                        Timeout = 30_000
                    });
                }
                catch (TimeoutException)
                {
                    // NetworkIdle timeout is okay — we may have captured what we need
                }
            });

        // Detect session expiry mid-run
        if (page.Url.Contains("/login") || page.Url.Contains("/sign-in"))
        {
            AnsiConsole.MarkupLine("[yellow]Session expired mid-run.[/] Clearing cache and re-authenticating...");
            ClaudeLoginHandler.ClearCache();

            var authenticated = await EnsureAuthenticatedAsync();
            if (!authenticated)
                return null;

            return await FetchAndDisplayAsync(page, discoverMode, cancellationToken, intervalSeconds);
        }

        if (discoverMode)
        {
            await Task.Delay(3000, cancellationToken);
            ConsoleRenderer.RenderDiscoveryResults(interceptor.DiscoveredResponses);
            AnsiConsole.MarkupLine("[dim]Tip: Look for an endpoint containing usage/limits data and update[/]");
            AnsiConsole.MarkupLine("[dim]     NetworkInterceptor.IsUsageResponse() with the exact URL pattern.[/]");
            return null;
        }

        InterceptedData intercepted;
        try
        {
            intercepted = await interceptor.WaitForDataAsync();
        }
        catch (TimeoutException)
        {
            ConsoleRenderer.RenderError(
                "Timed out waiting for the usage API response.\n" +
                "  Try [bold]--discover[/] to inspect what API responses are being received.\n" +
                "  Try [bold]--login[/] to re-authenticate if your session may have expired.");
            return null;
        }

        var data = UsageParser.Parse(intercepted);

        if (data is null)
        {
            ConsoleRenderer.RenderError(
                "Could not parse the usage API response.\n" +
                "  Run with [bold]--discover[/] to see the raw response and report this as a bug.");
            return null;
        }

        // Initial render — nextRefreshIn is the full interval
        ConsoleRenderer.Render(data, DateTimeOffset.Now, intervalSeconds, TimeSpan.FromSeconds(intervalSeconds));
        return data;
    }
}
