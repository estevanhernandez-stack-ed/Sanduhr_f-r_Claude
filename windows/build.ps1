# Full Windows build pipeline for Sanduhr für Claude.
#
# Usage:
#   ./build.ps1                   # full: PyInstaller + Inno Setup
#   ./build.ps1 -SkipInstaller    # PyInstaller only
#   ./build.ps1 -DebugBuild       # build with console window for tracebacks

param(
    [switch]$SkipInstaller,
    [switch]$DebugBuild
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)

# pyproject.toml is the single source of truth for version. Extract once here
# and propagate to both the PyInstaller-embedded version_info.txt and the
# Inno Setup preprocessor so a tag bump only needs one file change.
$pyproject = Get-Content "pyproject.toml" -Raw
if ($pyproject -match 'version\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"') {
    $Version = $Matches[1]
} else {
    throw "Could not extract version from pyproject.toml"
}
Write-Host "-> Version: $Version" -ForegroundColor Cyan

# Sync version_info.txt from pyproject.toml (replaces the hardcoded 2.0.0.0)
$viParts = ($Version -split '\.') + @('0')
$viTuple = $viParts -join ', '
$viString = "$Version.0"
$vi = Get-Content "version_info.txt" -Raw
$vi = $vi -replace 'filevers=\([0-9, ]+\)', "filevers=($viTuple)"
$vi = $vi -replace 'prodvers=\([0-9, ]+\)', "prodvers=($viTuple)"
$vi = $vi -replace "StringStruct\('FileVersion', '[0-9\.]+'\)", "StringStruct('FileVersion', '$viString')"
$vi = $vi -replace "StringStruct\('ProductVersion', '[0-9\.]+'\)", "StringStruct('ProductVersion', '$viString')"
Set-Content "version_info.txt" -Value $vi -NoNewline

Write-Host "-> Cleaning previous build..." -ForegroundColor Cyan
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue build, dist

$SpecFile = "sanduhr.spec"
if ($DebugBuild) {
    Write-Host "-> Debug build (console window enabled)" -ForegroundColor Yellow
    (Get-Content $SpecFile) -replace "console=False", "console=True" | Set-Content "sanduhr-debug.spec"
    $SpecFile = "sanduhr-debug.spec"
}

Write-Host "-> Running PyInstaller..." -ForegroundColor Cyan
pyinstaller $SpecFile --clean --noconfirm
if ($LASTEXITCODE -ne 0) {
    throw "PyInstaller failed (exit $LASTEXITCODE)"
}

if ($DebugBuild) {
    Remove-Item "sanduhr-debug.spec" -ErrorAction SilentlyContinue
}

if (-not (Test-Path "dist/Sanduhr/Sanduhr.exe")) {
    throw "dist/Sanduhr/Sanduhr.exe not produced"
}

Write-Host "OK PyInstaller complete: dist/Sanduhr/" -ForegroundColor Green

if ($SkipInstaller) {
    Write-Host "-> Skipping Inno Setup (as requested)" -ForegroundColor Yellow
    return
}

# Inno Setup installs system-wide under Program Files when you have admin,
# or per-user under %LOCALAPPDATA%\Programs when you don't. Probe both.
$ISCCCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
)
$ISCC = $ISCCCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $ISCC) {
    throw "Inno Setup 6 not found in any of:`n$($ISCCCandidates -join "`n")`nInstall from https://jrsoftware.org/isdl.php or: winget install --id JRSoftware.InnoSetup"
}

Write-Host "-> Running Inno Setup (v$Version)..." -ForegroundColor Cyan
# /DMyAppVersion=X.Y.Z overrides the #define in Sanduhr.iss, so the output
# filename + installed app version always match pyproject.toml.
& $ISCC "/DMyAppVersion=$Version" "installer/Sanduhr.iss"
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed (exit $LASTEXITCODE)"
}

$Installer = Get-ChildItem "build/Sanduhr-Setup-v*.exe" | Select-Object -First 1
if (-not $Installer) {
    throw "Installer .exe not produced in build/"
}

$SizeMB = [math]::Round($Installer.Length / 1MB, 1)
Write-Host "OK Build complete: $($Installer.Name) ($SizeMB MB)" -ForegroundColor Green
Write-Host "   Test: Start-Process .\build\$($Installer.Name)"
