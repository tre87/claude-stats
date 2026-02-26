using ClaudeStats.Console.Browser;
using ClaudeStats.Console.Display;
using Spectre.Console;

namespace ClaudeStats.Console;

public static class Program
{
    private static CancellationTokenSource? _cts;

    public static async Task<int> Main(string[] args)
    {
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

        return await RunInternalAsync(args, discoverMode, intervalSeconds);
    }

    private static async Task<int> RunInternalAsync(string[] args, bool discoverMode, int intervalSeconds)
    {
        _cts = new CancellationTokenSource();
        System.Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            var runner = new AppRunner();
            await runner.RunAsync(discoverMode, intervalSeconds, _cts.Token);
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
            if (_cts.IsCancellationRequested)
            {
                ConsoleRenderer.RestoreCursor();
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Stopped.[/]");
                return 0;
            }

            ConsoleRenderer.RestoreCursor();
            ConsoleRenderer.RenderError($"Unexpected error: {Markup.Escape(ex.Message)}");
            if (args.Contains("--verbose"))
                AnsiConsole.WriteException(ex);
            return 1;
        }
        finally
        {
            System.Console.CancelKeyPress -= OnCancelKeyPress;
            _cts.Dispose();
            _cts = null;
        }
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;  // prevent immediate process kill
        _cts?.Cancel();
    }
}
