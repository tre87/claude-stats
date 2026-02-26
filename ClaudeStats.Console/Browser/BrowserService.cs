using Microsoft.Playwright;

namespace ClaudeStats.Console.Browser;

public sealed class BrowserService : IAsyncDisposable
{
    private IBrowserContext? _context;
    private IPlaywright? _playwright;

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }

        _playwright?.Dispose();
    }

    public async Task<IPage> CreateAuthenticatedPageAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _context = await ClaudeLoginHandler.CreateAuthenticatedContextAsync(_playwright);
        return _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
    }
}