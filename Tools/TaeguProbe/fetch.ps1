$ErrorActionPreference = 'Stop'
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$headers = @{
    'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36'
    'Accept' = 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8'
}
$null = Invoke-WebRequest -Uri 'https://www.imc-companies.com/TaeguTec/ttkCatalog/' -WebSession $session -Headers $headers -UseBasicParsing
$itemUrl = 'https://www.imc-companies.com/TaeguTec/ttkCatalog/item.aspx?cat=6127491&fnum=10101&mapp=ML&app=401&GFSTYP=M&isoD=1'
$r = Invoke-WebRequest -Uri $itemUrl -WebSession $session -Headers $headers -UseBasicParsing
Write-Host "Status:" $r.StatusCode "Length:" $r.Content.Length
$out = Join-Path (Split-Path -Parent $PSScriptRoot) '..\Data\taegu_item.html'
$r.Content | Out-File -Encoding utf8 $out
Write-Host "Saved:" $out
