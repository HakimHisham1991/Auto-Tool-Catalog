using AutoToolCatalog.Data;
using AutoToolCatalog.Hubs;
using AutoToolCatalog.Models;
using AutoToolCatalog.Services;
using AutoToolCatalog.Services.Kennametal;
using AutoToolCatalog.Services.Seco;
using AutoToolCatalog.Services.Sandvik;
using AutoToolCatalog.Services.Walter;
using AutoToolCatalog.Services.TaeguTec;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

var localSettingsPath = Path.Combine(
    builder.Environment.ContentRootPath,
    $"appsettings.{builder.Environment.EnvironmentName}.local.json");
if (File.Exists(localSettingsPath))
    builder.Configuration.AddJsonFile(localSettingsPath, optional: true, reloadOnChange: true);

builder.Services.AddRazorPages();
builder.Services.AddSignalR();

builder.Services.AddSingleton<IProcessSessionStore, ProcessSessionStore>();
builder.Services.AddSingleton<ICatalogRepository, CatalogRepository>();
builder.Services.AddScoped<IExcelService, ExcelService>();
builder.Services.AddScoped<SecoProductDataProvider>();
builder.Services.AddScoped<KennametalProductDataProvider>();
builder.Services.AddScoped<SandvikProductDataProvider>();
builder.Services.AddScoped<WalterProductDataProvider>();
builder.Services.AddScoped<TaeguTecProductDataProvider>();
builder.Services.AddScoped<ProductDataProviderRegistry>();
builder.Services.AddScoped<IScraperService, ScraperService>();
builder.Services.AddSingleton<ISecoGlobalIdStore>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var cfg = sp.GetRequiredService<IConfiguration>();
    var dbPath = cfg["CatalogDb:Path"];
    if (string.IsNullOrWhiteSpace(dbPath))
        dbPath = Path.Combine(env.ContentRootPath, "Data", "catalog.db");
    var excelPath = Path.Combine(env.ContentRootPath, "Data", "SECO_GLOBAL_ID.xlsx");
    var store = new SecoGlobalIdStore(excelPath, dbPath);
    store.Initialize();
    return store;
});
builder.Services.AddSingleton<ITaeguTecCatalogStore>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var cfg = sp.GetRequiredService<IConfiguration>();
    var dbPath = cfg["CatalogDb:Path"];
    if (string.IsNullOrWhiteSpace(dbPath))
        dbPath = Path.Combine(env.ContentRootPath, "Data", "catalog.db");
    var excelPath = Path.Combine(env.ContentRootPath, "Data", "TAEGUTEC_CATALOG_NO.xlsx");
    var store = new TaeguTecCatalogStore(excelPath, dbPath);
    store.Initialize();
    return store;
});
builder.Services.AddSingleton<SecoHttpSession>();
builder.Services.AddScoped<ISecoApiClient, SecoApiClient>();
builder.Services.AddScoped<IKennametalApiClient, KennametalApiClient>();
builder.Services.AddScoped<ISandvikApiClient, SandvikApiClient>();
builder.Services.AddScoped<IWalterApiClient, WalterApiClient>();
builder.Services.AddSingleton<TaeguTecHttpSession>();
var taeguBrowserbaseKey = builder.Configuration["TaeguTec:BrowserbaseApiKey"]
    ?? Environment.GetEnvironmentVariable("BROWSERBASE_API_KEY");
var taeguBrowserbaseProject = builder.Configuration["TaeguTec:BrowserbaseProjectId"]
    ?? Environment.GetEnvironmentVariable("BROWSERBASE_PROJECT_ID");
var taeguBrowserbaseMaxConcurrency =
    builder.Configuration.GetValue<int?>("TaeguTec:BrowserbaseMaxConcurrency") ?? 2;
var taeguCatalogExcelPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "TAEGUTEC_CATALOG_NO.xlsx");
builder.Services.AddSingleton(_ => new TaeguTecRuntimeInfo
{
    UsesBrowserbase = !string.IsNullOrWhiteSpace(taeguBrowserbaseKey),
    CatalogExcelPath = taeguCatalogExcelPath
});
if (!string.IsNullOrWhiteSpace(taeguBrowserbaseKey))
{
    builder.Services.AddSingleton<ITaeguTecItemFetcher>(sp =>
        new TaeguTecBrowserbaseFetcher(
            taeguBrowserbaseKey,
            taeguBrowserbaseProject,
            sp.GetService<ILogger<TaeguTecBrowserbaseFetcher>>(),
            taeguBrowserbaseMaxConcurrency));
}
else
{
    builder.Services.AddSingleton<ITaeguTecItemFetcher>(sp => sp.GetRequiredService<TaeguTecHttpSession>());
}
builder.Services.AddScoped<ITaeguTecApiClient, TaeguTecApiClient>();
builder.Services.AddHttpClient("SECO", c =>
{
    c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    c.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
});

builder.Services.AddHttpClient("KENNAMETAL", c =>
{
    c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    c.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
});

builder.Services.AddHttpClient("SANDVIK", c =>
{
    c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    c.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
});

builder.Services.AddHttpClient("WALTER", c =>
{
    c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    c.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
});

var app = builder.Build();

var catalog = app.Services.GetRequiredService<ICatalogRepository>();
catalog.Initialize();

var secoGlobalIds = app.Services.GetRequiredService<ISecoGlobalIdStore>();
app.Logger.LogInformation("SECO master list loaded: {Count} global IDs", secoGlobalIds.Count);

var taegutecCatalog = app.Services.GetRequiredService<ITaeguTecCatalogStore>();
var taeguRuntime = app.Services.GetRequiredService<TaeguTecRuntimeInfo>();
app.Logger.LogInformation("TaeguTec master list loaded: {Count} catalog numbers", taegutecCatalog.Count);
app.Logger.LogInformation("TaeguTec fetch mode: {Mode}",
    taeguRuntime.UsesBrowserbase ? "Browserbase cloud browser" : "HTTP (Cloudflare-limited)");
if (taegutecCatalog.Count == 0)
    app.Logger.LogWarning("TaeguTec master catalog is empty — check Data/TAEGUTEC_CATALOG_NO.xlsx at {Path}", taeguRuntime.CatalogExcelPath);
if (!taeguRuntime.UsesBrowserbase)
    app.Logger.LogWarning("TaeguTec Browserbase API key not configured — TaeguTec rows will fail behind Cloudflare. Set BROWSERBASE_API_KEY or appsettings.Production.local.json.");

// Playwright browser install at startup can crash or hang on shared hosting (MonsterASP).
// SECO uses HttpClient only; Playwright remains for Kennametal browser fallback.
if (Environment.GetEnvironmentVariable("DISABLE_PLAYWRIGHT_INSTALL") != "true" &&
    (app.Environment.IsDevelopment() ||
     app.Configuration.GetValue("Playwright:InstallOnStartup", false)))
{
    PlaywrightBootstrap.EnsureBrowsersInstalled(app.Logger);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();
app.MapHub<ProcessingHub>("/hubs/processing");

app.MapPost("/api/upload", async (HttpRequest req, IExcelService excel, IProcessSessionStore store, CancellationToken ct) =>
{
    if (!req.HasFormContentType || req.Form.Files.Count == 0)
        return Results.BadRequest("No file uploaded");
    var file = req.Form.Files[0];
    if (Path.GetExtension(file.FileName).ToLowerInvariant() != ".xlsx")
        return Results.BadRequest("Only .xlsx files are accepted");
    await using var stream = file.OpenReadStream();
    var records = await excel.ImportAsync(stream, ct);
    var session = new ProcessSession
    {
        SourceFileName = file.FileName,
        Records = records,
        Progress = new ProcessingProgress { Total = records.Count }
    };
    store.Set(session);
    return Results.Ok(new { sessionId = session.Id, count = records.Count });
});

app.MapPost("/api/process/{sessionId}", (string sessionId, IProcessSessionStore store, IServiceScopeFactory scopeFactory, IHubContext<ProcessingHub> hub) =>
{
    var session = store.Get(sessionId);
    if (session == null) return Results.NotFound();

    var cts = new CancellationTokenSource();
    session.Cts = cts;
    var progress = new SignalRProgressReporter(hub, sessionId);

    Func<int, ToolRecord, Task> onRecordDone = async (index, record) =>
    {
        await hub.Clients.Group(sessionId).SendAsync("RecordUpdated", new
        {
            index,
            columns = session.PropertyColumns,
            row = ToRowDto(record, session.PropertyColumns)
        });
    };

    _ = Task.Run(async () =>
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var scraper = scope.ServiceProvider.GetRequiredService<IScraperService>();
        await scraper.ProcessAsync(session, progress, onRecordDone, cts.Token);
        await hub.Clients.Group(sessionId).SendAsync("ColumnsUpdated", session.PropertyColumns);
    });
    return Results.Accepted();
});

app.MapPost("/api/stop/{sessionId}", (string sessionId, IProcessSessionStore store) =>
{
    var session = store.Get(sessionId);
    if (session == null) return Results.NotFound();
    session.Cts?.Cancel();
    return Results.Ok(new { stopped = true });
});

app.MapGet("/api/records/{sessionId}", (string sessionId, IProcessSessionStore store) =>
{
    var session = store.Get(sessionId);
    if (session == null) return Results.NotFound();
    return Results.Ok(new
    {
        columns = session.PropertyColumns,
        rows = session.Records.Select(r => ToRowDto(r, session.PropertyColumns))
    });
});

app.MapGet("/api/progress/{sessionId}", (string sessionId, IProcessSessionStore store) =>
{
    var session = store.Get(sessionId);
    if (session == null) return Results.NotFound();
    return Results.Ok(session.Progress);
});

app.MapGet("/api/sample", async (IExcelService excel, CancellationToken ct) =>
{
    var sampleRecords = new List<ToolRecord>
    {
        new() { No = 1, ToolDescription = "553055Z3.0-SIRON-A", ProcurementChannel = "SECO" },
        new() { No = 2, ToolDescription = "H1TE4RA0400N006HBR025M", ProcurementChannel = "KENNAMETAL" }
    };
    var bytes = await excel.ExportAsync(sampleRecords, Array.Empty<string>(), ct);
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ToolCatalog_Sample.xlsx");
});

app.MapGet("/api/export/{sessionId}", async (string sessionId, IProcessSessionStore store, IExcelService excel, CancellationToken ct) =>
{
    var session = store.Get(sessionId);
    if (session == null) return Results.NotFound();
    var bytes = await excel.ExportAsync(session.Records, session.PropertyColumns, ct);
    var baseName = string.IsNullOrWhiteSpace(session.SourceFileName)
        ? "ToolCatalog"
        : Path.GetFileNameWithoutExtension(session.SourceFileName);
    var downloadName = $"{baseName}_updated.xlsx";
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", downloadName);
});

app.Run();

static object ToRowDto(ToolRecord record, IReadOnlyList<string> columns)
{
    var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var column in columns)
        properties[column] = record.Properties.TryGetValue(column, out var value) ? value : "#N/A";

    return new
    {
        record.No,
        record.ToolDescription,
        procurementChannel = record.ProcurementChannel,
        webpageLink = record.WebpageLink,
        properties
    };
}
