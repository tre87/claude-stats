using System.Text.Json;
using Microsoft.Playwright;
using Spectre.Console;
using Cookie = Microsoft.Playwright.Cookie;

namespace ClaudeStats.Console.Browser;

/// <summary>
/// Handles authentication with claude.ai using a persistent Firefox browser context.
/// On first run (or when the session expires), a headed Firefox window is opened so
/// the user can log in interactively. The session cookies are then saved to disk and
/// reused on subsequent runs without any manual interaction.
/// </summary>
public static class ClaudeLoginHandler
{
    private const int WindowWidth = 1100;
    private const int WindowHeight = 720;
    private const string LoginUrl = "https://claude.ai/login";
    private const string UsageUrl = "https://claude.ai/settings/usage";

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "claude-stats");

    private static readonly string FirefoxProfileDir = Path.Combine(DataDir, "browser-firefox");
    private static readonly string CookieFile = Path.Combine(DataDir, "cookies.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool HasCachedSession => File.Exists(CookieFile);

    public static void ClearCache()
    {
        if (File.Exists(CookieFile))
            File.Delete(CookieFile);

        if (Directory.Exists(FirefoxProfileDir))
            Directory.Delete(FirefoxProfileDir, true);
    }

    /// <summary>
    /// Silently verifies the cached session by navigating to the usage page
    /// with a headless Firefox. Returns true if the session is still valid.
    /// </summary>
    public static async Task<bool> TrySilentSessionCheckAsync()
    {
        if (!HasCachedSession)
            return false;

        using var playwright = await Playwright.CreateAsync();
        Directory.CreateDirectory(FirefoxProfileDir);

        await using var context = await playwright.Firefox.LaunchPersistentContextAsync(
            FirefoxProfileDir,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = true,
                IgnoreHTTPSErrors = true,
                AcceptDownloads = false,
                FirefoxUserPrefs = new Dictionary<string, object>
                {
                    ["browser.sessionstore.resume_from_crash"] = false,
                    ["browser.startup.page"] = 0,
                }
            });

        await RestoreCookiesAsync(context);

        var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();

        try
        {
            await page.GotoAsync(UsageUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.Commit,
                Timeout = 15_000
            });
        }
        catch
        {
            return false;
        }

        return !page.Url.Contains("/login") && !page.Url.Contains("/sign-in");
    }

    /// <summary>
    /// Opens a visible Firefox window (no address bar) and navigates to claude.ai/login.
    /// Waits indefinitely for the user to complete login, then saves cookies and closes.
    /// </summary>
    public static async Task<string?> InteractiveLoginAsync()
    {
        using var playwright = await Playwright.CreateAsync();
        Directory.CreateDirectory(FirefoxProfileDir);

        PrepareFirefoxProfile(FirefoxProfileDir);

        await using var context = await playwright.Firefox.LaunchPersistentContextAsync(
            FirefoxProfileDir,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false,
                IgnoreHTTPSErrors = true,
                AcceptDownloads = false,
                ColorScheme = ColorScheme.Dark,
                ViewportSize = new ViewportSize { Width = WindowWidth, Height = WindowHeight },
                FirefoxUserPrefs = new Dictionary<string, object>
                {
                    ["ui.systemUsesDarkTheme"] = 1,
                    ["browser.theme.content-theme"] = 0,
                    ["browser.theme.toolbar-theme"] = 0,
                    ["toolkit.legacyUserProfileCustomizations.stylesheets"] = true,
                    ["browser.toolbars.bookmarks.visibility"] = "never",
                    ["browser.tabs.inTitlebar"] = 0,
                    ["browser.chrome.toolbar_tips"] = false,
                    ["browser.urlbar.suggest.searches"] = false,
                    ["browser.sessionstore.resume_from_crash"] = false,
                    ["browser.startup.page"] = 0,
                }
            });

        await RestoreCookiesAsync(context);

        var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();
        await page.GotoAsync(LoginUrl, new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

        // Center the Firefox window on screen
        var (x, y) = ScreenHelper.GetCenteredWindowPosition(WindowWidth, WindowHeight);
        try { await page.EvaluateAsync($"window.moveTo({x}, {y})"); } catch { /* best effort */ }

        // If already logged in (restored cookies were valid), skip waiting
        if (!page.Url.Contains("/login") && !page.Url.Contains("/sign-in"))
        {
            var key = await ExtractSessionKeyAsync(context);
            await SaveCookiesAsync(context);
            await context.CloseAsync();
            return key;
        }

        AnsiConsole.MarkupLine("[yellow]A Firefox window has opened — please log in to claude.ai.[/]");
        AnsiConsole.MarkupLine("[dim]The window will close automatically once login is complete.[/]");

        try
        {
            // Wait up to 5 minutes for the user to finish logging in
            await page.WaitForURLAsync(
                url => !url.Contains("/login") && !url.Contains("/sign-in"),
                new PageWaitForURLOptions { Timeout = 300_000 });
        }
        catch (TimeoutException)
        {
            AnsiConsole.MarkupLine("[red]Login timed out after 5 minutes.[/]");
            await context.CloseAsync();
            return null;
        }

        // Let auth cookies settle before we read them
        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 }); }
        catch { /* best effort */ }

        var sessionKey = await ExtractSessionKeyAsync(context);
        await SaveCookiesAsync(context);
        await context.CloseAsync();
        return sessionKey;
    }

    /// <summary>
    /// Creates a headless Firefox context with restored cookies for fetching usage data.
    /// The caller is responsible for disposing the returned context.
    /// </summary>
    public static async Task<IBrowserContext> CreateAuthenticatedContextAsync(IPlaywright playwright)
    {
        Directory.CreateDirectory(FirefoxProfileDir);

        var context = await playwright.Firefox.LaunchPersistentContextAsync(
            FirefoxProfileDir,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = true,
                IgnoreHTTPSErrors = true,
                AcceptDownloads = false,
                FirefoxUserPrefs = new Dictionary<string, object>
                {
                    ["browser.sessionstore.resume_from_crash"] = false,
                    ["browser.startup.page"] = 0,
                }
            });

        await RestoreCookiesAsync(context);
        return context;
    }

    private static async Task<string?> ExtractSessionKeyAsync(IBrowserContext context)
    {
        var cookies = await context.CookiesAsync(["https://claude.ai"]);
        return cookies.FirstOrDefault(c => c.Name == "sessionKey")?.Value;
    }

    private static async Task SaveCookiesAsync(IBrowserContext context)
    {
        Directory.CreateDirectory(DataDir);
        var cookies = await context.CookiesAsync();
        var json = JsonSerializer.Serialize(cookies, JsonOptions);
        await File.WriteAllTextAsync(CookieFile, json);
    }

    private static async Task RestoreCookiesAsync(IBrowserContext context)
    {
        if (!File.Exists(CookieFile))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(CookieFile);
            var cookies = JsonSerializer.Deserialize<List<Cookie>>(json);
            if (cookies is { Count: > 0 })
                await context.AddCookiesAsync(cookies);
        }
        catch (JsonException)
        {
            File.Delete(CookieFile);
        }
    }

    /// <summary>
    /// Pre-seeds the Firefox profile with userChrome.css and user.js so that
    /// preferences (especially stylesheet loading) are active on first launch.
    /// Matches the Bio.Auth pattern exactly.
    /// </summary>
    private static void PrepareFirefoxProfile(string profileDir)
    {
        // Copy userChrome.css into profile/chrome/ — this hides the address bar and toolbars
        var chromeDir = Path.Combine(profileDir, "chrome");
        Directory.CreateDirectory(chromeDir);
        var cssSource = Path.Combine(AppContext.BaseDirectory, "userChrome.css");
        File.Copy(cssSource, Path.Combine(chromeDir, "userChrome.css"), overwrite: true);

        // Write user.js — Firefox reads this on startup before prefs.js, ensuring our prefs win
        var userJs =
            "user_pref(\"toolkit.legacyUserProfileCustomizations.stylesheets\", true);\n" +
            "user_pref(\"ui.systemUsesDarkTheme\", 1);\n" +
            "user_pref(\"browser.theme.content-theme\", 0);\n" +
            "user_pref(\"browser.theme.toolbar-theme\", 0);\n" +
            "user_pref(\"browser.toolbars.bookmarks.visibility\", \"never\");\n" +
            "user_pref(\"browser.tabs.inTitlebar\", 0);\n" +
            "user_pref(\"browser.chrome.toolbar_tips\", false);\n" +
            "user_pref(\"browser.urlbar.suggest.searches\", false);\n" +
            "user_pref(\"browser.sessionstore.resume_from_crash\", false);\n" +
            "user_pref(\"browser.startup.page\", 0);\n";

        File.WriteAllText(Path.Combine(profileDir, "user.js"), userJs);
    }
}
