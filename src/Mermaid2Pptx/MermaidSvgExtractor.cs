using Microsoft.Playwright;

namespace Mermaid2Pptx;

public sealed class MermaidSvgExtractor
{
    public async Task<List<ExtractedSvg>> ExtractAsync(
        string htmlPath,
        string slideSelector,
        string svgSelector,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(htmlPath))
        {
            throw new FileNotFoundException("HTML input file was not found.", htmlPath);
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);

        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1600, Height = 900 }
        });

        var uri = new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri;
        await page.GotoAsync(uri, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 60_000
        });

        await WaitForMermaidAsync(page, svgSelector, cancellationToken);

        var result = new List<ExtractedSvg>();
        var slideHandles = await page.QuerySelectorAllAsync(slideSelector);
        if (slideHandles.Count == 0)
        {
            slideHandles = [await page.QuerySelectorAsync("body") ?? throw new InvalidOperationException("Unable to find document body.")];
        }

        for (var slideIndex = 0; slideIndex < slideHandles.Count; slideIndex++)
        {
            var slide = slideHandles[slideIndex];
            var slideBox = await slide.BoundingBoxAsync();
            var svgHandles = new List<IElementHandle>();
            foreach (var candidate in await slide.QuerySelectorAllAsync(svgSelector))
            {
                if (await IsOuterSvgAsync(candidate))
                {
                    svgHandles.Add(candidate);
                }
            }
            for (var svgIndex = 0; svgIndex < svgHandles.Count; svgIndex++)
            {
                var svg = svgHandles[svgIndex];
                var markup = await svg.EvaluateAsync<string>("el => el.outerHTML");
                var box = await svg.BoundingBoxAsync();
                if (!string.IsNullOrWhiteSpace(markup))
                {
                    result.Add(new ExtractedSvg(
                        slideIndex,
                        svgIndex,
                        markup,
                        box is not null && slideBox is not null ? box.X - slideBox.X : box?.X,
                        box is not null && slideBox is not null ? box.Y - slideBox.Y : box?.Y,
                        box?.Width,
                        box?.Height,
                        slideBox?.Width,
                        slideBox?.Height));
                }
            }
        }

        return result;
    }

    private static async Task<bool> IsOuterSvgAsync(IElementHandle svg) =>
        await svg.EvaluateAsync<bool>("el => !el.parentElement?.closest('svg')");

    private static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
    {
        try
        {
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });
        }
        catch (PlaywrightException exception) when (LooksLikeMissingBundledChromium(exception))
        {
            var errors = new List<string>
            {
                $"bundled chromium: {exception.Message.Split(Environment.NewLine)[0]}"
            };

            foreach (var channel in new[] { "msedge", "chrome" })
            {
                try
                {
                    Console.Error.WriteLine($"Bundled Playwright Chromium was not found; trying local browser channel '{channel}'.");
                    var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = true,
                        Channel = channel
                    });
                    Console.Error.WriteLine($"Using local browser channel '{channel}'.");
                    return browser;
                }
                catch (PlaywrightException channelException)
                {
                    errors.Add($"{channel}: {channelException.Message.Split(Environment.NewLine)[0]}");
                }
            }

            throw new InvalidOperationException(
                "Unable to launch bundled Chromium or local Edge/Chrome channels. " +
                "Install Playwright Chromium with `pwsh src/Mermaid2Pptx/bin/Debug/net8.0/playwright.ps1 install chromium`, " +
                "or install Microsoft Edge / Google Chrome. Details: " + string.Join(" | ", errors),
                exception);
        }
    }

    private static bool LooksLikeMissingBundledChromium(PlaywrightException exception)
    {
        var message = exception.Message;
        return message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Please run the following command to download new browsers", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitForMermaidAsync(IPage page, string svgSelector, CancellationToken cancellationToken)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 30_000 });
            await page.WaitForFunctionAsync(
                @"selector => {
                    const svgs = Array.from(document.querySelectorAll(selector));
                    const pendingMermaid = document.querySelector('.mermaid:not([data-processed=""true""])');
                    const explicitDone = window.__MERMAID_DONE__ === true || window.mermaid === undefined;
                    return svgs.length > 0 && !pendingMermaid && (explicitDone || svgs.every(svg => svg.children.length > 0));
                }",
                svgSelector,
                new PageWaitForFunctionOptions { Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            var count = await page.Locator(svgSelector).CountAsync();
            if (count == 0)
            {
                throw;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
