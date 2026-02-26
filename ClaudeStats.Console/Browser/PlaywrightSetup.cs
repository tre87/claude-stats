namespace ClaudeStats.Console.Browser;

public static class PlaywrightSetup
{
    public static void EnsureBrowsersInstalled()
    {
        var exitCode = Microsoft.Playwright.Program.Main(["install", "firefox"]);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("Failed to install Playwright Firefox. Run 'playwright install firefox' manually.");
        }
    }
}