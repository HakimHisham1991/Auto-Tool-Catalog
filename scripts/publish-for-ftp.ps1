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

    $playwrightDir = Join-Path $out ".playwright"
    if (Test-Path $playwrightDir) {
        Remove-Item -Recurse -Force $playwrightDir
        Write-Host "Removed .playwright binaries (not needed for SECO HttpClient mode)" -ForegroundColor Green
    }

    # Production on MonsterASP does not load appsettings.Development.json. If a Browserbase key
    # exists locally, embed it into publish output as appsettings.Production.local.json (gitignored).
    $devSettingsPath = Join-Path $projectRoot 'appsettings.Development.json'
    if (Test-Path $devSettingsPath) {
        try {
            $dev = Get-Content $devSettingsPath -Raw | ConvertFrom-Json
            $bbKey = $dev.TaeguTec.BrowserbaseApiKey
            if ($bbKey) {
                $bbProject = $dev.TaeguTec.BrowserbaseProjectId
                if (-not $bbProject) { $bbProject = '' }
                $bbConcurrency = $dev.TaeguTec.BrowserbaseMaxConcurrency
                if (-not $bbConcurrency) { $bbConcurrency = 2 }
                $prodLocal = @{
                    TaeguTec = @{
                        BrowserbaseApiKey = [string]$bbKey
                        BrowserbaseProjectId = [string]$bbProject
                        BrowserbaseMaxConcurrency = [int]$bbConcurrency
                    }
                } | ConvertTo-Json -Depth 3
                $prodLocalPath = Join-Path $out 'appsettings.Production.local.json'
                Set-Content -Path $prodLocalPath -Value $prodLocal -Encoding UTF8
                Write-Host "Wrote appsettings.Production.local.json (Browserbase key for server deploy)" -ForegroundColor Green
            }
        }
        catch {
            Write-Host "Could not read Browserbase key from appsettings.Development.json: $_" -ForegroundColor Yellow
        }
    }

    Write-Host ""
    Write-Host "Ready to upload:" -ForegroundColor Green
    Write-Host "  $out"
    Write-Host "Copy all files inside that folder to your MonsterASP wwwroot (FTP port 21 or WebFTP)."
    Write-Host ""
    Write-Host "Reminder - MonsterASP environment variables (if not using appsettings.Production.local.json):" -ForegroundColor Yellow
    Write-Host "  BROWSERBASE_API_KEY        = <your key>   # optional if appsettings.Production.local.json was written above"
    Write-Host "  DISABLE_PLAYWRIGHT_INSTALL = true          # prevents Node/Chromium install on startup"
    Write-Host "  ASPNETCORE_ENVIRONMENT     = Production"
    Write-Host "After startup, the log should read: 'TaeguTec fetch mode: Browserbase cloud browser'." -ForegroundColor Yellow
}
finally {
    Pop-Location
}
