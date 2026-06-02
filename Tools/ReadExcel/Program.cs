using ClosedXML.Excel;

var path = args.Length > 0 ? args[0] : @"c:\Users\Public\Documents\Auto-Tool-Catalog\TEST\Tool Database_Test_3_Sandvik.xlsx";
using var wb = new XLWorkbook(path);
var ws = wb.Worksheet(1);
foreach (var row in ws.RangeUsed()!.Rows().Take(15))
{
    var cells = row.CellsUsed().Select(c => $"{c.Address.ColumnLetter}:{c.GetString()}");
    Console.WriteLine(string.Join(" | ", cells));
}
