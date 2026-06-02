# Build Release output for manual upload to MonsterASP wwwroot (FTP / WebFTP).
param(
    [string] $OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'publish_clean')
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$out = [System.IO.Path]::GetFullPath($OutputPath)

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Path $out -Force | Out-Null

Push-Location $projectRoot
try {
    # UseAppHost=false: publish DLL only (MonsterASP OutOfProcess + EXE disables the app pool).
    dotnet publish AutoToolCatalog.csproj --configuration Release --output $out -p:UseAppHost=false
    Write-Host ""
    Write-Host "Ready to upload:" -ForegroundColor Green
    Write-Host "  $out"
    Write-Host "Copy all files inside that folder to your MonsterASP wwwroot (FTP port 21 or WebFTP)."
}
finally {
    Pop-Location
}
