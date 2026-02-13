using AutoToolCatalog.Hubs;
using AutoToolCatalog.Models;
using AutoToolCatalog.Services;
using AutoToolCatalog.Services.SupplierParsers;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSignalR();

builder.Services.AddSingleton<IProcessSessionStore, ProcessSessionStore>();
builder.Services.AddScoped<IExcelService, ExcelService>();
builder.Services.AddScoped<IScraperService, ScraperService>();

builder.Services.AddHttpClient("SECO", c => { c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"); });
builder.Services.AddHttpClient("KENNAMETAL", c => { c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"); });
builder.Services.AddHttpClient("SANDVIK", c => { c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"); });
builder.Services.AddHttpClient("WALTER", c => { c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"); });

builder.Services.AddScoped<ISupplierParser, SecoParser>();
builder.Services.AddScoped<ISupplierParser, KennametalParser>();
builder.Services.AddScoped<ISupplierParser, SandvikParser>();
builder.Services.AddScoped<ISupplierParser, WalterParser>();

var app = builder.Build();

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
    var session = new ProcessSession { Records = records, Progress = new ProcessingProgress { Total = records.Count } };
    store.Set(session);
    return Results.Ok(new { sessionId = session.Id, count = records.Count });
});

app.MapPost("/api/process/{sessionId}", async (string sessionId, IProcessSessionStore store, IScraperService scraper, IHubContext<ProcessingHub> hub, CancellationToken ct) =>
{
    var session = store.Get(sessionId);
    if (session == null) return Results.NotFound();
    var progress = new SignalRProgressReporter(hub, sessionId);
    _ = Task.Run(async () =>
    {
        await scraper.ProcessAsync(session, progress, CancellationToken.None);
    }, ct);
    return Results.Accepted();
});

app.MapGet("/api/records/{sessionId}", (string sessionId, IProcessSessionStore store) =>
{
    var session = store.Get(sessionId);
    if (session == null) return Results.NotFound();
    var preview = session.Records.Select(r => new
    {
        r.No,
        r.ToolDescription,
        r.TypeOfTool,
        r.ShankBoreDiameter,
        r.ToolDiameter,
        r.CornerRad,
        r.FluteCuttingEdgeLength,
        r.OverallLength,
        r.PeripheralCuttingEdgeCount,
        r.ProcurementChannel
    });
    return Results.Ok(preview);
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
        new() { No = 1, ToolDescription = "SECO FCPM 160404 EPMW H10", TypeOfTool = "Endmill", ProcurementChannel = "SECO" },
        new() { No = 2, ToolDescription = "Kennametal KCD 12", TypeOfTool = "Drill", ProcurementChannel = "KENNAMETAL" }
    };
    var bytes = await excel.ExportAsync(sampleRecords, ct);
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ToolCatalog_Sample.xlsx");
});

app.MapGet("/api/export/{sessionId}", async (string sessionId, IProcessSessionStore store, IExcelService excel, CancellationToken ct) =>
{
    var session = store.Get(sessionId);
    if (session == null) return Results.NotFound();
    var bytes = await excel.ExportAsync(session.Records, ct);
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ToolCatalog_Enriched.xlsx");
});

app.Run();
