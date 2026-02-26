using ClaudeStats.Console;
using ClaudeStats.Console.Browser;
using ClaudeStats.Console.Display;
using Spectre.Console;

if (args.Contains("--help") || args.Contains("-h"))
{
    ConsoleRenderer.RenderHelp();
    return 0;
}

// --login / --reconfigure: clear the cached session so the next run opens Firefox for login
if (args.Contains("--login") || args.Contains("--reconfigure"))
{
    ClaudeLoginHandler.ClearCache();
    AnsiConsole.MarkupLine("[yellow]Saved session cleared.[/] A Firefox window will open to log in.");
    AnsiConsole.WriteLine();
}

var discoverMode = args.Contains("--discover");

// Parse --interval N (seconds), default 60
var intervalSeconds = 60;
var intervalIdx = Array.IndexOf(args, "--interval");
if (intervalIdx >= 0 && intervalIdx + 1 < args.Length &&
    int.TryParse(args[intervalIdx + 1], out var parsed) && parsed > 0)
{
    intervalSeconds = parsed;
}

// Wire up Ctrl+C to cancel the refresh loop gracefully
using var cts = new CancellationTokenSource();
System.Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;  // prevent immediate process kill
    cts.Cancel();
};

try
{
    var runner = new AppRunner();
    await runner.RunAsync(discoverMode, intervalSeconds, cts.Token);
    return 0;
}
catch (OperationCanceledException)
{
    ConsoleRenderer.RestoreCursor();
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[dim]Stopped.[/]");
    return 0;
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    // Playwright throws a plain Exception (not OperationCanceledException) when the
    // browser is disposed mid-operation after Ctrl+C — treat those as clean exits too.
    if (cts.IsCancellationRequested)
    {
        ConsoleRenderer.RestoreCursor();
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Stopped.[/]");
        return 0;
    }

    ConsoleRenderer.RestoreCursor();
    ConsoleRenderer.RenderError($"Unexpected error: {ex.Message}");
    if (args.Contains("--verbose"))
        AnsiConsole.WriteException(ex);
    return 1;
}
