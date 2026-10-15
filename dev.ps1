# Dev loop: stop the running padx, rebuild, and start it again.
#   .\dev.ps1          build + restart
#   .\dev.ps1 -Test    run unit tests first, abort on failure
#   .\dev.ps1 -Stop    just stop the running instance
param([switch]$Test, [switch]$Stop)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$running = Get-Process -Name padx -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping padx..." -ForegroundColor Yellow
    # Ask nicely first so the app gets a chance to save its settings.
    $running | ForEach-Object { $_.CloseMainWindow() | Out-Null }
    $running | Wait-Process -Timeout 3 -ErrorAction SilentlyContinue
    Get-Process -Name padx -ErrorAction SilentlyContinue | Stop-Process -Force
}
if ($Stop) { return }

if ($Test) {
    dotnet test --nologo -v q
    if ($LASTEXITCODE -ne 0) { Write-Host "Tests failed." -ForegroundColor Red; exit 1 }
}

dotnet build src/Paddock.App --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit 1 }

Start-Process (Join-Path $PSScriptRoot 'src\Paddock.App\bin\Debug\net8.0-windows\padx.exe')
Write-Host "padx started. Log: $env:LOCALAPPDATA\padx\paddock.log" -ForegroundColor Green
