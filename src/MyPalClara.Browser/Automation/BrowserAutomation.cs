using MyPalClara.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace MyPalClara.Browser.Automation;

/// <summary>
/// CDP browser automation using Playwright.
/// Manages browser lifecycle and provides page interaction methods.
/// </summary>
public sealed class BrowserAutomation : IAsyncDisposable
{
    private readonly ClaraConfig _config;
    private readonly ILogger<BrowserAutomation> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _currentPage;

    public bool IsRunning => _browser?.IsConnected ?? false;

    public BrowserAutomation(ClaraConfig config, ILogger<BrowserAutomation> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task LaunchAsync(CancellationToken ct = default)
    {
        _playwright = await Playwright.CreateAsync();

        var browserType = _config.Browser.BrowserType.ToLowerInvariant() switch
        {
            "firefox" => _playwright.Firefox,
            "webkit" => _playwright.Webkit,
            _ => _playwright.Chromium,
        };

        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = _config.Browser.Headless,
        };

        _browser = await browserType.LaunchAsync(launchOptions);

        var contextOptions = new BrowserNewContextOptions();
        if (!string.IsNullOrEmpty(_config.Browser.UserDataDir))
        {
            // For persistent context, use LaunchPersistentContextAsync instead
            _context = await _browser.NewContextAsync(contextOptions);
        }
        else
        {
            _context = await _browser.NewContextAsync(contextOptions);
        }

        _context.SetDefaultTimeout(_config.Browser.DefaultTimeoutMs);
        _currentPage = await _context.NewPageAsync();

        _logger.LogInformation("Browser launched ({Type}, headless={Headless})",
            _config.Browser.BrowserType, _config.Browser.Headless);
    }

    public async Task<string> NavigateAsync(string url, CancellationToken ct = default)
    {
        EnsurePage();
        var response = await _currentPage!.GotoAsync(url);
        _logger.LogInformation("Navigated to {Url} (status={Status})", url, response?.Status);
        return $"Navigated to {url} — status {response?.Status}";
    }

    public async Task<string> GetSnapshotAsync(CancellationToken ct = default)
    {
        EnsurePage();
        var title = await _currentPage!.TitleAsync();
        var url = _currentPage.Url;

        // Get a text-based accessibility snapshot
        var content = await _currentPage.ContentAsync();
        var textContent = await _currentPage.EvaluateAsync<string>("() => document.body.innerText");

        // Truncate for LLM context
        if (textContent.Length > 5000)
            textContent = textContent[..5000] + "\n[TRUNCATED]";

        return $"Title: {title}\nURL: {url}\n\n{textContent}";
    }

    public async Task<string> ClickAsync(string selector, CancellationToken ct = default)
    {
        EnsurePage();
        await _currentPage!.ClickAsync(selector);
        return $"Clicked: {selector}";
    }

    public async Task<string> TypeAsync(string selector, string text, CancellationToken ct = default)
    {
        EnsurePage();
        await _currentPage!.FillAsync(selector, text);
        return $"Typed into {selector}: {text}";
    }

    public async Task<byte[]> ScreenshotAsync(CancellationToken ct = default)
    {
        EnsurePage();
        return await _currentPage!.ScreenshotAsync(new PageScreenshotOptions { FullPage = true });
    }

    public async Task<string> EvaluateAsync(string expression, CancellationToken ct = default)
    {
        EnsurePage();
        var result = await _currentPage!.EvaluateAsync<string>(expression);
        return result;
    }

    private void EnsurePage()
    {
        if (_currentPage is null)
            throw new InvalidOperationException("Browser not launched. Call LaunchAsync() first.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();

        _logger.LogInformation("Browser disposed");
    }
}
