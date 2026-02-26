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
        ConsoleRenderer.RenderStartup("Checking session...");

        // Watch for a keypress during startup — cancels and clears the saved session
        using var startupCts = new CancellationTokenSource();
        var keyWatcher = Task.Run(async () =>
        {
            while (!startupCts.Token.IsCancellationRequested)
            {
                if (System.Console.KeyAvailable)
                {
                    System.Console.ReadKey(intercept: true);
                    await startupCts.CancelAsync();
                    return;
                }
                await Task.Delay(50, startupCts.Token).ContinueWith(_ => { });
            }
        });

        // One-time auth check before entering the loop
        var authenticated = await EnsureAuthenticatedAsync();
        if (!authenticated)
        {
            ConsoleRenderer.RenderError("Authentication failed or was cancelled.");
            return;
        }

        if (startupCts.IsCancellationRequested)
        {
            ClaudeLoginHandler.ClearCache();
            AnsiConsole.MarkupLine("[yellow]Session cleared.[/] Run again to log in.");
            return;
        }

        ConsoleRenderer.RenderStartup("Connecting to browser...");

        // Reuse the same browser context and page across refreshes
        await using var browserService = new BrowserService();
        IPage page;
        try
        {
            page = await browserService.CreateAuthenticatedPageAsync();
        }
        catch (Exception ex)
        {
            ConsoleRenderer.RenderError($"Failed to launch browser: {Markup.Escape(ex.Message)}");
            return;
        }

        if (startupCts.IsCancellationRequested)
        {
            ClaudeLoginHandler.ClearCache();
            AnsiConsole.MarkupLine("[yellow]Session cleared.[/] Run again to log in.");
            return;
        }

        await startupCts.CancelAsync(); // stop the watcher once we're into the main loop

        // Main refresh loop — runs until Ctrl+C
        UsageData? lastGoodData = null;
        DateTimeOffset lastFetchedAt = default;

        while (!cancellationToken.IsCancellationRequested)
        {
            var (data, warning) = await FetchAndDisplayAsync(page, discoverMode, cancellationToken, intervalSeconds, lastGoodData);

            if (discoverMode)
            {
                break;
            }

            string? lastWarning;
            if (data is not null)
            {
                lastGoodData = data;
                lastFetchedAt = DateTimeOffset.Now;
                lastWarning = null;
            }
            else
            {
                // Fetch failed — keep showing last good data with a warning
                lastWarning = warning ?? "Update failed — retrying next interval";
                if (lastGoodData is null)
                {
                    break; // No data at all yet, nothing to show
                }
            }

            // Tick every second, re-rendering with live countdowns — no API call needed
            var nextFetchAt = DateTimeOffset.Now.AddSeconds(intervalSeconds);
            var isInteractiveTerminal = !System.Console.IsOutputRedirected && AnsiConsole.Profile.Capabilities.Ansi;
            try
            {
                // Render once immediately
                ConsoleRenderer.Render(lastGoodData!, lastFetchedAt, intervalSeconds, nextFetchAt - DateTimeOffset.Now, lastWarning);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var remaining = nextFetchAt - DateTimeOffset.Now;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    await Task.Delay(1000, cancellationToken);

                    // Only re-render every second in a real terminal — avoid spamming non-interactive output
                    if (isInteractiveTerminal)
                    {
                        ConsoleRenderer.Render(lastGoodData!, lastFetchedAt, intervalSeconds, nextFetchAt - DateTimeOffset.Now, lastWarning);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     Checks for a valid cached session. If missing or expired, opens Firefox
    ///     for interactive login. Returns true when authenticated.
    /// </summary>
    private static async Task<bool> EnsureAuthenticatedAsync()
    {
        if (ClaudeLoginHandler.HasCachedSession)
        {
            var valid = await ClaudeLoginHandler.TrySilentSessionCheckAsync();

            if (valid)
            {
                return true;
            }

            ConsoleRenderer.RenderStartup("Session expired — opening Firefox to log in again...");
        }
        else
        {
            ConsoleRenderer.RenderStartup("No saved session — opening Firefox to log in...");
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

    private static async Task<(UsageData? Data, string? Warning)> FetchAndDisplayAsync(
        IPage page,
        bool discoverMode,
        CancellationToken cancellationToken,
        int intervalSeconds,
        UsageData? previousData)
    {
        var interceptor = new NetworkInterceptor(discoverMode);
        interceptor.Register(page);

        ConsoleRenderer.RenderStartup("Fetching usage data from claude.ai...");

        try
        {
            await page.GotoAsync(UsageUrl,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30_000
                });
        }
        catch (TimeoutException)
        {
            // NetworkIdle timeout is okay — we may have captured what we need
        }

        // Detect session expiry mid-run
        if (page.Url.Contains("/login") || page.Url.Contains("/sign-in"))
        {
            AnsiConsole.MarkupLine("[yellow]Session expired mid-run.[/] Clearing cache and re-authenticating...");
            ClaudeLoginHandler.ClearCache();

            var authenticated = await EnsureAuthenticatedAsync();
            if (!authenticated)
            {
                return (null, "Re-authentication failed");
            }

            return await FetchAndDisplayAsync(page, discoverMode, cancellationToken, intervalSeconds, previousData);
        }

        if (discoverMode)
        {
            await Task.Delay(3000, cancellationToken);
            ConsoleRenderer.RenderDiscoveryResults(interceptor.DiscoveredResponses);
            AnsiConsole.MarkupLine("[dim]Tip: Look for an endpoint containing usage/limits data and update[/]");
            AnsiConsole.MarkupLine("[dim]     NetworkInterceptor.IsUsageResponse() with the exact URL pattern.[/]");
            return (null, null);
        }

        InterceptedData intercepted;
        try
        {
            intercepted = await interceptor.WaitForDataAsync();
        }
        catch (TimeoutException)
        {
            return (null, "Update timed out — will retry next interval");
        }

        var data = UsageParser.Parse(intercepted);

        if (data is null)
        {
            return (null, "Could not parse API response — will retry next interval");
        }

        return (data, null);
    }
}