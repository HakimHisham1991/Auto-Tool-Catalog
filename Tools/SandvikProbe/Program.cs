using Microsoft.Playwright;
using System.Text.Json;

var designation = args.Length > 0 ? args[0] : "1K354-1000-XD 1730";
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
var page = await browser.NewPageAsync();

page.Request += (_, request) =>
{
    if (request.Url.Contains("/api/", StringComparison.OrdinalIgnoreCase) &&
        request.Method == "POST")
        Console.WriteLine($"REQ POST {request.Url}\n{request.PostData}\n---");
};

page.Response += async (_, response) =>
{
    if (!response.Url.Contains("/api/", StringComparison.OrdinalIgnoreCase)) return;
    if (response.Status != 200) return;
    var text = await response.TextAsync();
    if (text.Contains("product", StringComparison.OrdinalIgnoreCase) &&
        (text.Contains("Id", StringComparison.Ordinal) || text.Contains("ORDCODE", StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine($"RESP {response.Url}\n{text[..Math.Min(1500, text.Length)]}\n---");
};

await page.GotoAsync("https://www.sandvik.coromant.com/en-gb/", new() { Timeout = 120_000 });
foreach (var sel in new[] { "#onetrust-accept-btn-handler", "button:has-text('Accept all')" })
{
    var btn = page.Locator(sel);
    if (await btn.CountAsync() > 0) { await btn.First.ClickAsync(); break; }
}

foreach (var sel in new[] { "input[type='search']", "input[placeholder*='Search' i]", "#search-input" })
{
    var input = page.Locator(sel);
    if (await input.CountAsync() == 0) continue;
    await input.First.FillAsync(designation);
    await input.First.PressAsync("Enter");
    break;
}

await page.WaitForTimeoutAsync(10000);
Console.WriteLine($"Final URL: {page.Url}");

var m = System.Text.RegularExpressions.Regex.Match(page.Url, @"[?&]m=(\d+)");
if (m.Success) Console.WriteLine($"MaterialId from URL: {m.Groups[1].Value}");
