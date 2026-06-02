using AutoToolCatalog.Models;
using AutoToolCatalog.Services.TaeguTec;
using Microsoft.Playwright;

const string sampleHtml = """
    <html><body>
    <span>Family Designation: HSF 6...XLT</span> | High precision solid carbide end mills
    <span>Item Designation: HSF 6050XLT 250</span>
    <table><tr><td>DC</td><td>OAL</td><td>APMX</td><td>DCONMS</td></tr>
    <tr><td>5.00</td><td>80.00</td><td>25.00</td><td>6.00</td></tr></table>
    </body></html>
    """;
var sample = TaeguTecHtmlParser.Parse(sampleHtml);
Console.WriteLine($"Sample parse: {sample.Properties.Count} props, DC={sample.Properties.GetValueOrDefault("TAEG_DC")}");

var url =
    "https://www.imc-companies.com/TaeguTec/ttkCatalog/item.aspx?cat=6127491&fnum=10101&mapp=ML&app=401&GFSTYP=M&isoD=1";

Microsoft.Playwright.Program.Main(["install", "chromium"]);
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
var page = await browser.NewPageAsync();
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
await page.WaitForSelectorAsync("text=Item Designation", new() { Timeout = 90_000 });
var html = await page.ContentAsync();
var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Data", "taegu_item.html");
path = Path.GetFullPath(path);
await File.WriteAllTextAsync(path, html);
Console.WriteLine($"Saved {html.Length} chars to {path}");

var parsed = TaeguTecHtmlParser.Parse(html);
Console.WriteLine($"CatalogId={parsed.CatalogId} Designation={parsed.ItemDesignation}");
Console.WriteLine($"Props={parsed.Properties.Count}");
foreach (var (k, v) in parsed.Properties.Take(15))
    Console.WriteLine($"  {k}={v}");

// Search test
var searchUrl =
    "https://www.imc-companies.com/TaeguTec/ttkCatalog/search.aspx?searchText=HSF%206050XLT%20250";
await page.GotoAsync(searchUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
var links = await page.Locator("a[href*='item.aspx?cat=']").AllAsync();
Console.WriteLine($"Search links: {links.Count}");
foreach (var link in links.Take(5))
{
    var href = await link.GetAttributeAsync("href");
    var text = (await link.InnerTextAsync()).Trim();
    Console.WriteLine($"  {text} -> {href}");
}
