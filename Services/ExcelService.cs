using AutoToolCatalog.Models;
using ClosedXML.Excel;

namespace AutoToolCatalog.Services;

public class ExcelService : IExcelService
{
    private static readonly string[] CoreHeaders =
    [
        "No.",
        "Tool Description",
        "Supplier",
        "Link"
    ];

    public async Task<List<ToolRecord>> ImportAsync(Stream stream, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);
            var headerRow = worksheet.Row(1);
            var columnMap = MapColumns(headerRow);

            var rows = worksheet.RangeUsed()?.Rows().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();
            var records = new List<ToolRecord>();
            var rowIndex = 2;

            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                var toolDescription = GetMappedString(row, columnMap, "tool description", 2);
                if (string.IsNullOrWhiteSpace(toolDescription))
                {
                    rowIndex++;
                    continue;
                }

                var record = new ToolRecord
                {
                    RowIndex = rowIndex,
                    No = GetMappedInt(row, columnMap, "no.", 1),
                    ToolDescription = toolDescription,
                    ProcurementChannel = GetSupplier(row, columnMap),
                    WebpageLink = GetMappedStringOrNull(row, columnMap, "link", 4)
                };
                records.Add(record);
                rowIndex++;
            }

            return records;
        }, ct);
    }

    public async Task<byte[]> ExportAsync(List<ToolRecord> records, IReadOnlyList<string> propertyColumns, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Tool Catalog");

            var headers = CoreHeaders.Concat(propertyColumns).ToList();
            for (var col = 0; col < headers.Count; col++)
                worksheet.Cell(1, col + 1).Value = headers[col];

            for (var i = 0; i < records.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                WriteRecordRow(worksheet, i + 2, records[i], propertyColumns);
            }

            var lastRow = Math.Max(1, records.Count + 1);
            var lastCol = headers.Count;
            var tableRange = worksheet.Range(1, 1, lastRow, lastCol);

            tableRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            tableRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            var table = tableRange.CreateTable("ToolCatalog");
            table.Theme = XLTableTheme.TableStyleMedium6;
            table.ShowAutoFilter = true;

            worksheet.Columns(1, lastCol).AdjustToContents();

            using var ms = new MemoryStream();
            workbook.SaveAs(ms, false);
            return ms.ToArray();
        }, ct);
    }

    private static void WriteRecordRow(IXLWorksheet worksheet, int row, ToolRecord r, IReadOnlyList<string> propertyColumns)
    {
        worksheet.Cell(row, 1).Value = r.No;
        worksheet.Cell(row, 2).Value = r.ToolDescription;
        worksheet.Cell(row, 3).Value = r.ProcurementChannel;

        var linkCell = worksheet.Cell(row, 4);
        linkCell.Value = r.WebpageLink ?? "";
        if (!string.IsNullOrWhiteSpace(r.WebpageLink))
            linkCell.SetHyperlink(new XLHyperlink(r.WebpageLink));

        for (var i = 0; i < propertyColumns.Count; i++)
        {
            var key = propertyColumns[i];
            worksheet.Cell(row, 5 + i).Value = r.Properties.TryGetValue(key, out var value) ? value : "#N/A";
        }
    }

    private static string GetSupplier(IXLRangeRow row, Dictionary<string, int> map)
    {
        var supplier = GetMappedString(row, map, "supplier", 0);
        if (!string.IsNullOrWhiteSpace(supplier) && LooksLikeSupplier(supplier))
            return supplier;

        supplier = GetMappedString(row, map, "procurement channel", 0);
        if (!string.IsNullOrWhiteSpace(supplier))
            return supplier;

        return GetMappedString(row, map, "supplier", 3);
    }

    private static bool LooksLikeSupplier(string value)
    {
        var upper = value.ToUpperInvariant();
        return upper.Contains("SECO") || upper.Contains("KENNAMETAL") || upper.Contains("SANDVIK") || upper.Contains("WALTER");
    }

    private static Dictionary<string, int> MapColumns(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var name = cell.GetString().Trim();
            if (!string.IsNullOrWhiteSpace(name))
                map[name] = cell.Address.ColumnNumber;
        }

        if (map.TryGetValue("Procurement channel", out var supplierCol) && !map.ContainsKey("Supplier"))
            map["Supplier"] = supplierCol;

        foreach (var (key, target) in new (string Key, string Target)[]
        {
            ("Webpage Link", "Link"),
            ("Webpage link", "Link"),
            ("URL", "Link"),
            ("Product URL", "Link"),
            ("Product Link", "Link")
        })
        {
            if (map.TryGetValue(key, out var linkCol) && !map.ContainsKey(target))
                map[target] = linkCol;
        }

        return map;
    }

    private static int GetMappedInt(IXLRangeRow row, Dictionary<string, int> map, string header, int fallbackCol) =>
        int.TryParse(GetMappedString(row, map, header, fallbackCol), out var v) ? v : 0;

    private static string GetMappedString(IXLRangeRow row, Dictionary<string, int> map, string header, int fallbackCol) =>
        row.Cell(map.TryGetValue(header, out var col) ? col : fallbackCol).GetString().Trim();

    private static string? GetMappedStringOrNull(IXLRangeRow row, Dictionary<string, int> map, string header, int fallbackCol)
    {
        var s = GetMappedString(row, map, header, fallbackCol);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
