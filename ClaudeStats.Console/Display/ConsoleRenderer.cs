using System.Reflection;
using ClaudeStats.Console.Models;
using Spectre.Console;

namespace ClaudeStats.Console.Display;

public static class ConsoleRenderer
{
    private static readonly string AppVersion =
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"v{v.Major}.{v.Minor}.{v.Build}"
            : "v?";

    private static string PlanLabel(PlanTier plan) => plan switch
    {
        PlanTier.Free    => "Free",
        PlanTier.Pro     => "Pro",
        PlanTier.Max     => "Max",
        PlanTier.Team    => "Team",
        _                => ""
    };

    private static string TitleMarkup(string? planLabel = null)
    {
        var plan = planLabel is not null ? $" [dim grey]|[/] [dodgerblue1]Plan: {Markup.Escape(planLabel)}[/]" : "";
        return $"[bold dodgerblue1]Claude Usage Statistics[/] [dim grey]|[/] [dodgerblue1]{AppVersion}[/]{plan}";
    }

    public static void Render(UsageData data, DateTimeOffset fetchedAt = default, int intervalSeconds = 60, TimeSpan nextRefreshIn = default, string? warning = null, string? refreshingSpinner = null)
    {
        // Hide cursor, jump to top-left, erase to end of screen — flicker-free redraw
        System.Console.Write("\x1b[?25l\x1b[H\x1b[J");
        AnsiConsole.WriteLine();
        var plan = PlanLabel(data.Plan);
        AnsiConsole.Write(new Rule(TitleMarkup(plan.Length > 0 ? plan : null)).RuleStyle("grey"));
        AnsiConsole.WriteLine();

        var anyRendered = false;

        if (data.FiveHour is not null)
        {
            RenderPeriod(data.FiveHour, AppColors.FiveHour);
            anyRendered = true;
        }

        if (data.SevenDay is not null)
        {
            RenderPeriod(data.SevenDay, AppColors.SevenDay);
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

        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        if (refreshingSpinner is not null)
        {
            // Show blue spinner + "Refreshing..." instead of the countdown
            AnsiConsole.MarkupLine($"  [dim]Updated {timeStr}  ·[/]  [dodgerblue1]{Markup.Escape(refreshingSpinner)} Refreshing...[/]  [dim]·  Ctrl+C to quit[/]");
        }
        else
        {
            var nextStr = nextRefreshIn == default || nextRefreshIn <= TimeSpan.Zero
                ? "now"
                : nextRefreshIn.TotalSeconds < 60
                    ? $"{(int)nextRefreshIn.TotalSeconds}s"
                    : $"{(int)nextRefreshIn.TotalMinutes}m {nextRefreshIn.Seconds}s";
            AnsiConsole.MarkupLine($"  [dim]Updated {timeStr}  ·  next refresh in [bold]{nextStr}[/]  ·  Ctrl+C to quit[/]");
        }

        if (warning is not null)
        {
            AnsiConsole.MarkupLine($"  [yellow]⚠ {Markup.Escape(warning)}[/]");
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>Draws the title rule + a single status line, no spinner.</summary>
    public static void RenderHeader(string status)
    {
        System.Console.Write("\x1b[H\x1b[J");
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(TitleMarkup()).RuleStyle("grey"));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(status)}[/]");
    }

    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    /// <summary>
    ///     Renders the title header and animates a spinner next to <paramref name="status"/>
    ///     until <paramref name="work"/> completes.
    /// </summary>
    public static async Task RenderStartupAsync(string status, Task work)
    {
        System.Console.Write("\x1b[?25l"); // hide cursor
        var frame = 0;

        // Draw the static parts once
        System.Console.Write("\x1b[H\x1b[J");
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(TitleMarkup()).RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // Spinner line is always line 4 (0-indexed: row 4) — we'll jump back to it each tick
        while (!work.IsCompleted)
        {
            var spinner = SpinnerFrames[frame % SpinnerFrames.Length];
            // Jump to line 4, clear line, rewrite — spinner in dodger blue, label in white
            System.Console.Write($"\x1b[4;1H\x1b[2K  \x1b[38;5;33m{spinner}\x1b[0m {Markup.Escape(status)}");
            frame++;
            await Task.Delay(80).ContinueWith(_ => { });
        }

        // Final state: checkmark in blue, label in white
        System.Console.Write($"\x1b[4;1H\x1b[2K  \x1b[38;5;33m✓\x1b[0m {Markup.Escape(status)}");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [dim]Press any key to cancel and clear saved session[/]");
        System.Console.Write("\x1b[?25h"); // restore cursor
    }

    /// <summary>
    ///     Re-renders the full display with a blue spinner in the footer while <paramref name="work"/> runs.
    ///     No-ops in non-interactive terminals (e.g. Rider output window).
    /// </summary>
    public static async Task RenderRefreshingAsync(
        Task work,
        bool isInteractiveTerminal,
        UsageData data,
        DateTimeOffset fetchedAt,
        int intervalSeconds,
        string? warning)
    {
        if (!isInteractiveTerminal)
        {
            await work;
            return;
        }

        System.Console.Write("\x1b[?25l"); // hide cursor
        var frame = 0;

        while (!work.IsCompleted)
        {
            var spinner = SpinnerFrames[frame % SpinnerFrames.Length];
            Render(data, fetchedAt, intervalSeconds, default, warning, spinner);
            frame++;
            await Task.Delay(200).ContinueWith(_ => { });
        }

        System.Console.Write("\x1b[?25h"); // restore cursor
    }

    /// <summary>Restores cursor visibility — call on exit to avoid a hidden cursor in the terminal.</summary>
    public static void RestoreCursor()
    {
        System.Console.Write("\x1b[?25h");
    }

    private static int ChartWidth() => 80;

    /// <summary>
    /// Writes a line where left starts at column 0 and right ends exactly at <paramref name="totalWidth"/>.
    /// <paramref name="leftPlain"/> and <paramref name="rightPlain"/> are the unstyled versions used for length calculation.
    /// </summary>
    private static void WriteAlignedLine(string leftPlain, string leftMarkup, string rightPlain, string rightMarkup, int totalWidth)
    {
        var gap = Math.Max(1, totalWidth - leftPlain.Length - rightPlain.Length);
        AnsiConsole.Markup($"{leftMarkup}{new string(' ', gap)}{rightMarkup}");
        AnsiConsole.WriteLine();
    }

    private static void RenderPeriod(UsagePeriod period, Color usedColor)
    {
        var used = period.Utilization;
        var remaining = Math.Max(0.0, 100.0 - used);
        var resetText = FormatResetTime(period.TimeUntilReset);

        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(period.Label)}[/]");

        var chart = new BreakdownChart().Width(ChartWidth()).HideTags();

        if (used > 0)
            chart.AddItem($"Used ({used:F1}%)", used, usedColor);

        if (remaining > 0)
            chart.AddItem($"Remaining ({remaining:F1}%)", remaining, AppColors.Remaining);
        else
            chart.AddItem("Full", 1, Color.Red); // sentinel so chart isn't empty

        AnsiConsole.Write(chart);

        // Left: coloured square + white label. Right: dim square + white label, right-aligned under bar.
        var usedColorName = usedColor.ToMarkup();
        var limitTag   = period.IsAtLimit ? "  [bold red]LIMIT REACHED[/]" : "";
        var leftText   = $"\u25a0 Used ({used:F1}%)";
        var leftMarkup = $"[{usedColorName}]\u25a0[/] Used ({used:F1}%){limitTag}";
        string rightText, rightMarkup;
        if (remaining > 0)
        {
            rightText   = $"\u25a0 Remaining ({remaining:F1}%)";
            rightMarkup = $"[{AppColors.RemainingMarkup}]\u25a0[/] Remaining ({remaining:F1}%)";
        }
        else
        {
            rightText   = "\u25a0 Full";
            rightMarkup = "[red]\u25a0[/] [red]Full[/]";
        }
        WriteAlignedLine(leftText, leftMarkup, rightText, rightMarkup, ChartWidth());
        AnsiConsole.MarkupLine($"  [dim]Resets in: {Markup.Escape(resetText)}[/]");
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
                .Width(ChartWidth())
                .HideTags()
                .AddItem($"Used ({used:F1}%)", used, AppColors.ExtraUsage)
                .AddItem($"Remaining ({remaining:F1}%)", remaining, AppColors.Remaining);

            AnsiConsole.Write(chart);

            var resetText   = FormatResetTime(extra.ResetsAt.HasValue
                ? extra.ResetsAt.Value - DateTimeOffset.UtcNow
                : null);
            var leftText    = $"\u25a0 Used ({used:F1}%)";
            var leftMarkup  = $"[{AppColors.ExtraUsageMarkup}]\u25a0[/] Used ({used:F1}%)";
            var rightText   = $"\u25a0 Remaining ({remaining:F1}%)";
            var rightMarkup = $"[{AppColors.RemainingMarkup}]\u25a0[/] Remaining ({remaining:F1}%)";
            WriteAlignedLine(leftText, leftMarkup, rightText, rightMarkup, ChartWidth());
            AnsiConsole.MarkupLine($"  [dim]Resets in: {Markup.Escape(resetText)}[/]");
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
            var monthlyLimit = limit.MonthlyCreditLimit.Value;   // minor units, e.g. 5000 = €50.00
            var spentUnits   = limit.CurrentSpend ?? 0;          // minor units spent

            // All three segments are computed in minor units, then passed to BreakdownChart
            // which scales them proportionally — so they must sum to monthlyLimit exactly.
            var balanceUnits = prepaid?.Amount is > 0
                ? Math.Min(prepaid.Amount.Value, Math.Max(0L, monthlyLimit - spentUnits))
                : 0L;
            var beyondUnits = Math.Max(0L, monthlyLimit - spentUnits - balanceUnits);

            var chart = new BreakdownChart().Width(ChartWidth()).HideTags();

            // Spent — bright yellow
            if (spentUnits > 0)
                chart.AddItem($"Spent ({limit.FormatAmount(spentUnits)})", spentUnits, AppColors.Spent);

            // Balance reach — darker yellow
            if (balanceUnits > 0)
                chart.AddItem($"Balance ({prepaid!.FormatAmount()})", balanceUnits, AppColors.Balance);

            // Beyond balance — dim grey
            if (beyondUnits > 0)
                chart.AddItem($"Limit", beyondUnits, AppColors.Remaining);

            // Guard: chart needs at least one item
            if (spentUnits == 0 && balanceUnits == 0)
                chart.AddItem($"Limit", monthlyLimit, AppColors.Remaining);

            AnsiConsole.Write(chart);

            // Left: yellow square + white "Spent (€X)". Right: dim square + white "Limit €50 (€X left)".
            var leftText    = $"\u25a0 Spent ({limit.FormatAmount(spentUnits)})";
            var leftMarkup  = $"[{AppColors.SpentMarkup}]\u25a0[/] Spent ({Markup.Escape(limit.FormatAmount(spentUnits))})";
            var rightPlain  = beyondUnits > 0
                ? $"\u25a0 Limit {limit.FormatAmount(monthlyLimit)} ({limit.FormatAmount(beyondUnits)} left)"
                : $"\u25a0 Limit {limit.FormatAmount(monthlyLimit)}";
            var rightMarkup = beyondUnits > 0
                ? $"[{AppColors.RemainingMarkup}]\u25a0[/] Limit {Markup.Escape(limit.FormatAmount(monthlyLimit))} ({Markup.Escape(limit.FormatAmount(beyondUnits))} left)"
                : $"[{AppColors.RemainingMarkup}]\u25a0[/] Limit {Markup.Escape(limit.FormatAmount(monthlyLimit))}";
            WriteAlignedLine(leftText, leftMarkup, rightPlain, rightMarkup, ChartWidth());
        }
        else if (limit.CurrentSpend.HasValue)
        {
            AnsiConsole.MarkupLine($"  [{AppColors.SpentMarkup}]{limit.FormatAmount(limit.CurrentSpend)} spent[/]  [dim](no limit set)[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("  [green]Enabled[/]  [dim]No spend recorded[/]");
        }

        // Prepaid balance — flush left with bar, same width alignment
        if (prepaid?.Amount is not null)
        {
            var balPlain  = $"\u25a0 Balance: {prepaid.FormatAmount()} remaining";
            var balMarkup = $"[{AppColors.BalanceMarkup}]\u25a0[/] Balance: {Markup.Escape(prepaid.FormatAmount())} remaining";
            AnsiConsole.Markup(balMarkup);
            AnsiConsole.WriteLine();
        }

        // Credit grant info inline if relevant
        if (grant is not null && (grant.Available || grant.Granted || grant.Eligible))
        {
            if (grant.Granted && grant.AmountMinorUnits.HasValue)
            {
                AnsiConsole.MarkupLine($"  [green]Credit grant:[/] {grant.FormatAmount()} granted");
            }
            else if (grant.Available)
            {
                AnsiConsole.MarkupLine("  [yellow]Credit grant:[/] available to claim");
            }
            else if (grant.Eligible)
            {
                AnsiConsole.MarkupLine("  [dim]Credit grant:[/] eligible");
            }
        }

        AnsiConsole.WriteLine();
    }

    private static string FormatResetTime(TimeSpan? timeUntil)
    {
        if (timeUntil is null)
        {
            return "[dim]at unknown time[/]";
        }

        if (timeUntil.Value.TotalSeconds <= 0)
            return "now";

        var parts = new List<string>();
        if (timeUntil.Value.Days > 0)
            parts.Add($"{timeUntil.Value.Days}d");
        if (timeUntil.Value.Hours > 0)
            parts.Add($"{timeUntil.Value.Hours}h");
        if (timeUntil.Value.Minutes > 0)
            parts.Add($"{timeUntil.Value.Minutes}m");
        if (timeUntil.Value.Seconds > 0 && timeUntil.Value.Days == 0)
            parts.Add($"{timeUntil.Value.Seconds}s");

        return string.Join(" ", parts);
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
            AnsiConsole.MarkupLine("  [dim]Preview:[/]");
            AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(preview)}[/]");
            AnsiConsole.WriteLine();
        }
    }

    public static void RenderError(string message)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[red]Error:[/] {message}");
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