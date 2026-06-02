$path = 'c:\Users\Public\Documents\Auto-Tool-Catalog\TEST_SAMPLE\Tool Database_Test_5_Taegutec.xlsx'
Add-Type -Path (Get-ChildItem -Recurse -Filter 'ClosedXML.dll' "$env:USERPROFILE\.nuget\packages\closedxml" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName) -ErrorAction SilentlyContinue
# Use ClosedXML from project output
$dll = Resolve-Path '..\..\bin\Debug\net10.0\ClosedXML.dll'
Add-Type -Path $dll
$wb = New-Object ClosedXML.Excel.XLWorkbook($path)
$ws = $wb.Worksheet(1)
$used = $ws.RangeUsed()
if ($null -eq $used) { Write-Host 'empty'; exit }
$row1 = $ws.Row(1)
foreach ($c in $row1.CellsUsed()) { Write-Host "H$($c.Address.ColumnNumber): $($c.GetString())" }
$last = $used.LastRow().RowNumber()
for ($r = 2; $r -le $last; $r++) {
    $cells = @()
    foreach ($c in 1..6) { $cells += $ws.Cell($r,$c).GetString() }
    Write-Host "R$r: $($cells -join ' | ')"
}
$wb.Dispose()
