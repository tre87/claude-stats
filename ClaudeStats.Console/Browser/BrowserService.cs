using Microsoft.Playwright;

namespace ClaudeStats.Console.Browser;

public sealed class BrowserService : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowserContext? _context;

    public async Task<IPage> CreateAuthenticatedPageAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _context = await ClaudeLoginHandler.CreateAuthenticatedContextAsync(_playwright);
        return _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();
        _playwright?.Dispose();
    }
}
