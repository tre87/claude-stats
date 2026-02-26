using ClaudeStats.Console.Models;
using Spectre.Console;

namespace ClaudeStats.Console.Display;

public static class ConsoleRenderer
{
    public static void Render(UsageData data, DateTimeOffset fetchedAt = default, int intervalSeconds = 60, TimeSpan nextRefreshIn = default)
    {
        // Hide cursor and jump to top-left — avoids the full-clear blink on every tick
        System.Console.Write("\x1b[?25l\x1b[H");
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold dodgerblue1]Claude Usage Statistics[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        var anyRendered = false;

        if (data.FiveHour is not null)
        {
            RenderPeriod(data.FiveHour, Color.DodgerBlue1);
            anyRendered = true;
        }

        if (data.SevenDay is not null)
        {
            RenderPeriod(data.SevenDay, Color.Green);
            anyRendered = true;
        }

        if (data.SevenDayOpus is not null)
        {
            RenderPeriod(data.SevenDayOpus, Color.MediumPurple1);
            anyRendered = true;
        }

        if (data.SevenDaySonnet is not null)
        {
            RenderPeriod(data.SevenDaySonnet, Color.SteelBlue1);
            anyRendered = true;
        }

        if (data.ExtraUsage is not null)
        {
            RenderExtraUsagePeriod(data.ExtraUsage);
            anyRendered = true;
        }

        if (data.OverageSpendLimit is not null)
        {
            RenderOverageSpendLimit(data.OverageSpendLimit, data.OverageCreditGrant, data.PrepaidCredits);
            anyRendered = true;
        }

        if (!anyRendered)
        {
            AnsiConsole.MarkupLine("[yellow]No usage data could be parsed.[/]");
            AnsiConsole.MarkupLine("[dim]Try running with [bold]--discover[/] to inspect raw API responses.[/]");
        }

        // Footer
        var timeStr = fetchedAt == default ? "" : fetchedAt.ToLocalTime().ToString("HH:mm:ss");
        var nextStr = nextRefreshIn == default || nextRefreshIn <= TimeSpan.Zero
            ? "now"
            : nextRefreshIn.TotalSeconds < 60
                ? $"{(int)nextRefreshIn.TotalSeconds}s"
                : $"{(int)nextRefreshIn.TotalMinutes}m {nextRefreshIn.Seconds}s";
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        AnsiConsole.MarkupLine($"  [dim]Updated {timeStr}  ·  next refresh in [bold]{nextStr}[/]  ·  Ctrl+C to quit[/]");
        AnsiConsole.WriteLine();

        // Restore cursor visibility after render
        System.Console.Write("\x1b[?25h");
    }

    /// <summary>Restores cursor visibility — call on exit to avoid a hidden cursor in the terminal.</summary>
    public static void RestoreCursor() => System.Console.Write("\x1b[?25h");

    private static void RenderPeriod(UsagePeriod period, Color usedColor)
    {
        var used = period.Utilization;
        var remaining = Math.Max(0.0, 100.0 - used);
        var resetText = FormatResetTime(period.TimeUntilReset);

        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(period.Label)}[/]");

        var chart = new BreakdownChart().Width(60).HideTagValues();

        if (used > 0)
            chart.AddItem($"Used ({used:F0}%)", used, usedColor);

        if (remaining > 0)
            chart.AddItem($"Remaining ({remaining:F0}%)", remaining, Color.Grey23);
        else
            chart.AddItem("Full", 1, Color.Red);  // sentinel so chart isn't empty

        AnsiConsole.Write(chart);

        var percentColor = used >= 100 ? "red" : used >= 75 ? "yellow" : "green";
        var limitTag = period.IsAtLimit ? "  [bold red]LIMIT REACHED[/]" : "";

        AnsiConsole.MarkupLine(
            $"  [{percentColor}]{used:F1}%[/] used{limitTag}  " +
            $"[dim]·  resets {resetText}[/]");

        AnsiConsole.WriteLine();
    }

    private static void RenderExtraUsagePeriod(ExtraUsagePeriod extra)
    {
        AnsiConsole.Write(new Rule("[bold]Extra Usage[/]").RuleStyle("grey"));

        if (extra.Utilization.HasValue)
        {
            var used = Math.Min(100.0, extra.Utilization.Value);
            var remaining = Math.Max(0.0, 100.0 - used);

            var chart = new BreakdownChart()
                .Width(60)
                .HideTagValues()
                .AddItem($"Used ({used:F0}%)", used, Color.Orange1)
                .AddItem($"Remaining ({remaining:F0}%)", remaining, Color.Grey23);

            AnsiConsole.Write(chart);

            var resetText = FormatResetTime(extra.ResetsAt.HasValue
                ? extra.ResetsAt.Value - DateTimeOffset.UtcNow
                : null);
            AnsiConsole.MarkupLine($"  [orange1]{used:F1}%[/] used  [dim]·  resets {resetText}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("  [dim]No extra usage data[/]");
        }

        AnsiConsole.WriteLine();
    }

    private static void RenderOverageSpendLimit(OverageSpendLimit limit, OverageCreditGrant? grant, PrepaidCredits? prepaid)
    {
        AnsiConsole.Write(new Rule("[bold]Extra Usage (Pay-per-use)[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        if (!limit.IsEnabled)
        {
            AnsiConsole.MarkupLine("  [dim]Not enabled[/]");
            AnsiConsole.WriteLine();
            return;
        }

        if (limit.SpendPercent.HasValue && limit.MonthlyCreditLimit.HasValue)
        {
            var used = limit.SpendPercent.Value;
            var remaining = Math.Max(0.0, 100.0 - used);

            var chart = new BreakdownChart()
                .Width(60)
                .HideTagValues()
                .AddItem($"Spent ({limit.FormatAmount(limit.CurrentSpend)})", used, Color.Orange1)
                .AddItem($"Remaining ({limit.FormatAmount(limit.Remaining)})", remaining, Color.Grey23);

            AnsiConsole.Write(chart);

            var percentColor = used >= 90 ? "red" : used >= 60 ? "yellow" : "orange1";
            AnsiConsole.MarkupLine(
                $"  [{percentColor}]{limit.FormatAmount(limit.CurrentSpend)} spent[/]  " +
                $"[dim]of {limit.FormatAmount(limit.MonthlyCreditLimit)} monthly limit[/]");
        }
        else if (limit.CurrentSpend.HasValue)
        {
            AnsiConsole.MarkupLine($"  [orange1]{limit.FormatAmount(limit.CurrentSpend)} spent[/]  [dim](no limit set)[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("  [green]Enabled[/]  [dim]No spend recorded[/]");
        }

        // Prepaid balance — the number that actually matters day-to-day
        if (prepaid?.Amount is not null)
        {
            var balanceColor = prepaid.Amount < 500 ? "red" : prepaid.Amount < 2000 ? "yellow" : "green";
            AnsiConsole.MarkupLine($"  [{balanceColor}]Balance: {prepaid.FormatAmount()}[/]  [dim]prepaid credits remaining[/]");
        }

        // Credit grant info inline if relevant
        if (grant is not null && (grant.Available || grant.Granted || grant.Eligible))
        {
            if (grant.Granted && grant.AmountMinorUnits.HasValue)
                AnsiConsole.MarkupLine($"  [green]Credit grant:[/] {grant.FormatAmount()} granted");
            else if (grant.Available)
                AnsiConsole.MarkupLine("  [yellow]Credit grant:[/] available to claim");
            else if (grant.Eligible)
                AnsiConsole.MarkupLine("  [dim]Credit grant:[/] eligible");
        }

        AnsiConsole.WriteLine();
    }

    private static string FormatResetTime(TimeSpan? timeUntil)
    {
        if (timeUntil is null)
            return "[dim]at unknown time[/]";

        if (timeUntil.Value.TotalSeconds <= 0)
            return "[green]now[/]";

        if (timeUntil.Value.TotalMinutes < 1)
            return "[yellow]in less than a minute[/]";

        var parts = new List<string>();
        if (timeUntil.Value.Days > 0) parts.Add($"{timeUntil.Value.Days}d");
        if (timeUntil.Value.Hours > 0) parts.Add($"{timeUntil.Value.Hours}h");
        if (timeUntil.Value.Minutes > 0) parts.Add($"{timeUntil.Value.Minutes}m");

        return $"in [bold]{string.Join(" ", parts)}[/]";
    }

    public static void RenderDiscoveryResults(IReadOnlyList<(string Url, string ContentType, string Preview)> responses)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold yellow]Discovery Mode — Intercepted API Responses[/]").RuleStyle("yellow"));
        AnsiConsole.WriteLine();

        if (responses.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No JSON API responses were intercepted.[/]");
            AnsiConsole.MarkupLine("[dim]The page may have loaded from cache or the session cookie may be invalid.[/]");
            return;
        }

        foreach (var (url, contentType, preview) in responses)
        {
            AnsiConsole.MarkupLine($"[bold dodgerblue1]{Markup.Escape(url)}[/]");
            AnsiConsole.MarkupLine($"  [dim]Content-Type: {Markup.Escape(contentType)}[/]");
            AnsiConsole.MarkupLine($"  [dim]Preview:[/]");
            AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(preview)}[/]");
            AnsiConsole.WriteLine();
        }
    }

    public static void RenderError(string message)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        AnsiConsole.WriteLine();
    }

    public static void RenderHelp()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]claude-stats[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Displays Claude.ai usage statistics as progress bars.");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Usage:[/]");
        AnsiConsole.MarkupLine("  dotnet run [[options]]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Options:[/]");
        AnsiConsole.MarkupLine("  [bold]--help[/]             Show this help message");
        AnsiConsole.MarkupLine("  [bold]--login[/]            Clear saved session and log in again via Firefox");
        AnsiConsole.MarkupLine("  [bold]--interval[/] [dim]<N>[/]    Refresh every N seconds (default: 60)");
        AnsiConsole.MarkupLine("  [bold]--discover[/]         Dump all intercepted API responses (for debugging)");
        AnsiConsole.MarkupLine("  [bold]--verbose[/]          Show full exception details on error");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Authentication:[/]");
        AnsiConsole.MarkupLine("  On first run a Firefox window opens automatically — just log in to claude.ai.");
        AnsiConsole.MarkupLine("  Your session is saved and reused on future runs without opening a browser.");
        AnsiConsole.MarkupLine("  Run [bold]--login[/] if your session expires or you want to switch accounts.");
        AnsiConsole.WriteLine();

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "claude-stats");
        AnsiConsole.MarkupLine($"  Session stored at: [dim]{dataDir}[/]");
        AnsiConsole.WriteLine();
    }
}
