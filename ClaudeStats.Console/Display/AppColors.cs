using Spectre.Console;

namespace ClaudeStats.Console.Display;

/// <summary>Centralised colour palette — all RGB values defined here.</summary>
public static class AppColors
{
    // Progress bar segments
    public static readonly Color Spent       = new(218, 165,  32); // golden yellow — used/spent
    public static readonly Color Balance     = new(150, 110,  10); // darker golden — prepaid balance reach
    public static readonly Color Remaining   = new( 60,  60,  60); // dark grey — unused/remaining limit

    // Period bars (5h / 7-day)
    public static readonly Color FiveHour    = new( 30, 144, 255); // dodger blue
    public static readonly Color SevenDay    = new( 50, 205,  50); // lime green
    public static readonly Color ExtraUsage  = new(255, 140,   0); // dark orange

    // Title
    public static readonly Color Title       = new( 30, 144, 255); // dodger blue

    // Markup helpers — Spectre Color.ToMarkup() returns the hex string usable in [color] tags
    public static string SpentMarkup      => Spent.ToMarkup();
    public static string BalanceMarkup    => Balance.ToMarkup();
    public static string RemainingMarkup  => Remaining.ToMarkup();
    public static string ExtraUsageMarkup => ExtraUsage.ToMarkup();
}
