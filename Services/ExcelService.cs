using AutoToolCatalog.Models;
using ClosedXML.Excel;

namespace AutoToolCatalog.Services;

public class ExcelService : IExcelService
{
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
                    TypeOfTool = GetString(row, 3),
                    ShankBoreDiameter = GetStringOrNull(row, 4),
                    ToolDiameter = GetStringOrNull(row, 5),
                    CornerRad = GetStringOrNull(row, 6),
                    FluteCuttingEdgeLength = GetStringOrNull(row, 7),
                    OverallLength = GetStringOrNull(row, 8),
                    PeripheralCuttingEdgeCount = GetStringOrNull(row, 9),
                    ProcurementChannel = GetString(row, 10)
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

            worksheet.Cell(1, 1).Value = "No.";
            worksheet.Cell(1, 2).Value = "Tool Description";
            worksheet.Cell(1, 3).Value = "Type of Tool";
            worksheet.Cell(1, 4).Value = "Shank Ø (DMM) / Bore Ø (DCB)";
            worksheet.Cell(1, 5).Value = "Tool Ø (DC)";
            worksheet.Cell(1, 6).Value = "Corner rad";
            worksheet.Cell(1, 7).Value = "Flute / Cutting edge length (APMXS)";
            worksheet.Cell(1, 8).Value = "Overall length (OAL / LF)";
            worksheet.Cell(1, 9).Value = "Peripheral cutting edge count";
            worksheet.Cell(1, 10).Value = "Procurement channel";

            for (var i = 0; i < records.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var r = records[i];
                var excelRow = i + 2;
                worksheet.Cell(excelRow, 1).Value = r.No;
                worksheet.Cell(excelRow, 2).Value = r.ToolDescription;
                worksheet.Cell(excelRow, 3).Value = r.TypeOfTool;
                worksheet.Cell(excelRow, 4).Value = r.ShankBoreDiameter ?? "#NA";
                worksheet.Cell(excelRow, 5).Value = r.ToolDiameter ?? "#NA";
                worksheet.Cell(excelRow, 6).Value = r.CornerRad ?? "#NA";
                worksheet.Cell(excelRow, 7).Value = r.FluteCuttingEdgeLength ?? "#NA";
                worksheet.Cell(excelRow, 8).Value = r.OverallLength ?? "#NA";
                worksheet.Cell(excelRow, 9).Value = r.PeripheralCuttingEdgeCount ?? "#NA";
                worksheet.Cell(excelRow, 10).Value = r.ProcurementChannel;
            }

            using var ms = new MemoryStream();
            workbook.SaveAs(ms, false);
            return ms.ToArray();
        }, ct);
    }

    private static int GetInt(IXLRangeRow row, int col) => int.TryParse(GetString(row, col), out var v) ? v : 0;
    private static string GetString(IXLRangeRow row, int col) => row.Cell(col).GetString().Trim();
    private static string? GetStringOrNull(IXLRangeRow row, int col)
    {
        var s = row.Cell(col).GetString().Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
