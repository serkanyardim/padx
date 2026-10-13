# Builds a release: the single-file app and the installer, both named with the
# version from Directory.Build.props.
#   .\build-release.ps1
# Output in artifacts\:
#   padx-setup-v<version>.exe     installer (per-user, no admin)
#   padx-v<version>-portable.exe  the app itself, no install needed
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found (winget install JRSoftware.InnoSetup).' }

Write-Host "Building padx $version..." -ForegroundColor Cyan
dotnet test --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

dotnet publish src/Paddock.App -c Release -p:PublishProfile=win-x64 --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

& $iscc /Q "/DAppVersion=$version" installer\padx.iss
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

# The installer compresses better from the plain build; the portable exe is
# published again with single-file compression, about a third of the size.
dotnet publish src/Paddock.App -c Release -p:PublishProfile=win-x64 -p:EnableCompressionInSingleFile=true `
    -p:PublishDir=..\..\artifacts\publish\portable\ --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }

Copy-Item artifacts\publish\portable\padx.exe "artifacts\padx-v$version-portable.exe" -Force

Get-ChildItem artifacts\padx-*$version* | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    '{0,-32} {1,7:N1} MB  SHA256 {2}' -f $_.Name, ($_.Length / 1MB), $hash
}
