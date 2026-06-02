using AutoToolCatalog.Models;
using ClosedXML.Excel;

namespace AutoToolCatalog.Services;

public class ExcelService : IExcelService
{
    private const int ColumnCount = 11;

    private static readonly string[] ExportHeaders =
    [
        "No.",
        "Tool Description",
        "Supplier",
        "Link",
        "Type",
        "Shank/Bore Ø",
        "Tool Ø",
        "Corner rad",
        "Flute length",
        "OAL",
        "Edge count"
    ];

    public async Task<List<ToolRecord>> ImportAsync(Stream stream, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);
            var rows = worksheet.RangeUsed()?.Rows().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();
            var records = new List<ToolRecord>();
            var rowIndex = 2;

            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                var record = new ToolRecord
                {
                    RowIndex = rowIndex,
                    No = GetInt(row, 1),
                    ToolDescription = GetString(row, 2),
                    ProcurementChannel = GetString(row, 3),
                    WebpageLink = GetStringOrNull(row, 4),
                    TypeOfTool = GetString(row, 5),
                    ShankBoreDiameter = GetStringOrNull(row, 6),
                    ToolDiameter = GetStringOrNull(row, 7),
                    CornerRad = GetStringOrNull(row, 8),
                    FluteCuttingEdgeLength = GetStringOrNull(row, 9),
                    OverallLength = GetStringOrNull(row, 10),
                    PeripheralCuttingEdgeCount = GetStringOrNull(row, 11)
                };
                records.Add(record);
                rowIndex++;
            }

            return records;
        }, ct);
    }

    public async Task<byte[]> ExportAsync(List<ToolRecord> records, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Tool Catalog");

            for (var col = 0; col < ExportHeaders.Length; col++)
                worksheet.Cell(1, col + 1).Value = ExportHeaders[col];

            for (var i = 0; i < records.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                WriteRecordRow(worksheet, i + 2, records[i]);
            }

            var lastRow = Math.Max(1, records.Count + 1);
            var tableRange = worksheet.Range(1, 1, lastRow, ColumnCount);

            tableRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            tableRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            var table = tableRange.CreateTable("ToolCatalog");
            table.Theme = XLTableTheme.TableStyleMedium6;
            table.ShowAutoFilter = true;

            worksheet.Columns(1, ColumnCount).AdjustToContents();

            using var ms = new MemoryStream();
            workbook.SaveAs(ms, false);
            return ms.ToArray();
        }, ct);
    }

    private static void WriteRecordRow(IXLWorksheet worksheet, int row, ToolRecord r)
    {
        worksheet.Cell(row, 1).Value = r.No;
        worksheet.Cell(row, 2).Value = r.ToolDescription;
        worksheet.Cell(row, 3).Value = r.ProcurementChannel;

        var linkCell = worksheet.Cell(row, 4);
        linkCell.Value = r.WebpageLink ?? "";
        if (!string.IsNullOrWhiteSpace(r.WebpageLink))
            linkCell.SetHyperlink(new XLHyperlink(r.WebpageLink));

        worksheet.Cell(row, 5).Value = r.TypeOfTool;
        worksheet.Cell(row, 6).Value = r.ShankBoreDiameter ?? "#NA";
        worksheet.Cell(row, 7).Value = r.ToolDiameter ?? "#NA";
        worksheet.Cell(row, 8).Value = r.CornerRad ?? "#NA";
        worksheet.Cell(row, 9).Value = r.FluteCuttingEdgeLength ?? "#NA";
        worksheet.Cell(row, 10).Value = r.OverallLength ?? "#NA";
        worksheet.Cell(row, 11).Value = r.PeripheralCuttingEdgeCount ?? "#NA";
    }

    private static int GetInt(IXLRangeRow row, int col) => int.TryParse(GetString(row, col), out var v) ? v : 0;
    private static string GetString(IXLRangeRow row, int col) => row.Cell(col).GetString().Trim();
    private static string? GetStringOrNull(IXLRangeRow row, int col)
    {
        var s = row.Cell(col).GetString().Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
