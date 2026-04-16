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

$ISCC = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $ISCC)) {
    throw "Inno Setup 6 not found at $ISCC -- install from https://jrsoftware.org/isdl.php"
}

Write-Host "-> Running Inno Setup..." -ForegroundColor Cyan
& $ISCC "installer/Sanduhr.iss"
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
