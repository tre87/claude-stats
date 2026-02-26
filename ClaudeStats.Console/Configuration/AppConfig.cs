namespace ClaudeStats.Console.Configuration;

/// <summary>
/// Persisted application settings. Session authentication is now handled
/// via the browser cookie file managed by ClaudeLoginHandler — no manual
/// session key entry required.
/// </summary>
public sealed class AppConfig
{
    // Reserved for future per-user settings (e.g. refresh interval, colour theme).
}
