# Build Sanduhr.msix from the PyInstaller one-folder output.
#
# Assumes `./build.ps1 -SkipInstaller` already produced `dist/Sanduhr/`.
# Emits `build/Sanduhr-v{version}.msix`.
#
# Flags:
#   -SelfSign    Also self-sign the MSIX with a local cert matching the
#                manifest's Publisher CN. Required to sideload-install on
#                your own machine for testing. NOT used for Store submission
#                -- leave the MSIX unsigned and let Store ingestion sign it.
#
# Usage (from windows/):
#   ./msix/make-msix.ps1                # unsigned (Store-ready)
#   ./msix/make-msix.ps1 -SelfSign      # self-signed (local sideload test)

[CmdletBinding()]
param(
    [switch]$SelfSign
)

$ErrorActionPreference = "Stop"
$WindowsDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $WindowsDir

# -- Version from pyproject.toml, 4-part (MSIX requires A.B.C.D) --
$pyproject = Get-Content "pyproject.toml" -Raw
if ($pyproject -match 'version\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"') {
    $Version = "$($Matches[1]).0"
} else {
    throw "Could not extract version from pyproject.toml"
}
Write-Host "-> MSIX version: $Version" -ForegroundColor Cyan

# -- Locate Windows SDK's MakeAppx + SignTool --
$SdkBin = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter "10.*" -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName "x64" } |
    Where-Object { Test-Path (Join-Path $_ "MakeAppx.exe") } |
    Select-Object -First 1

if (-not $SdkBin) {
    throw "Windows SDK not found. Install from https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/ (need MakeAppx.exe + SignTool.exe)"
}
$MakeAppx = Join-Path $SdkBin "MakeAppx.exe"
$SignTool = Join-Path $SdkBin "SignTool.exe"
Write-Host "-> SDK: $SdkBin" -ForegroundColor Cyan

# -- Sanity: dist/Sanduhr/ must exist --
if (-not (Test-Path "dist/Sanduhr/Sanduhr.exe")) {
    throw "dist/Sanduhr/Sanduhr.exe not found -- run './build.ps1 -SkipInstaller' first"
}

# -- Stage: copy PyInstaller output + manifest + images into one tree --
$Stage = Join-Path $WindowsDir "msix/_stage"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $Stage
New-Item -ItemType Directory -Force $Stage | Out-Null

Write-Host "-> Staging files..." -ForegroundColor Cyan
Copy-Item "dist/Sanduhr/*" $Stage -Recurse -Force
Copy-Item "msix/Images" $Stage -Recurse -Force

# Inject version into manifest template.
$manifest = Get-Content "msix/Package.appxmanifest.template" -Raw
$manifest = $manifest -replace '\{\{VERSION\}\}', $Version
Set-Content (Join-Path $Stage "AppxManifest.xml") -Value $manifest -Encoding UTF8

# -- Pack --
$OutDir = Join-Path $WindowsDir "build"
New-Item -ItemType Directory -Force $OutDir | Out-Null
$MsixPath = Join-Path $OutDir "Sanduhr-v$Version.msix"
Remove-Item $MsixPath -ErrorAction SilentlyContinue

Write-Host "-> Packing MSIX..." -ForegroundColor Cyan
& $MakeAppx pack /d $Stage /p $MsixPath /nv /o
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx pack failed (exit $LASTEXITCODE)"
}

# -- Optional local self-sign --
if ($SelfSign) {
    Write-Host "-> Self-signing for local sideload test..." -ForegroundColor Yellow
    $Subject = "CN=177BCE59-0966-4975-9962-10E36652141F"
    $Cert = Get-ChildItem -Path Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $Subject } |
        Select-Object -First 1
    if (-not $Cert) {
        Write-Host "   No matching cert found; creating one..." -ForegroundColor Yellow
        $Cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $Subject `
            -KeyUsage DigitalSignature `
            -FriendlyName "Sanduhr MSIX Dev Cert" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
        Write-Host "   Created cert thumbprint: $($Cert.Thumbprint)"
        Write-Host "   NOTE: To sideload-install the signed MSIX, you must trust this cert once:"
        Write-Host "     Export-Certificate -Cert Cert:\CurrentUser\My\$($Cert.Thumbprint) -FilePath sanduhr-dev.cer"
        Write-Host "     Then import sanduhr-dev.cer into 'Trusted People' in Local Computer certs (certlm.msc)"
    }
    & $SignTool sign /fd SHA256 /a /sha1 $Cert.Thumbprint $MsixPath
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed (exit $LASTEXITCODE)"
    }
}

$SizeMB = [math]::Round((Get-Item $MsixPath).Length / 1MB, 1)
Write-Host ""
Write-Host "OK Built $MsixPath ($SizeMB MB)" -ForegroundColor Green
if ($SelfSign) {
    Write-Host "   Sideload-install (after cert import):  Add-AppxPackage $MsixPath" -ForegroundColor Gray
} else {
    Write-Host "   Unsigned -- upload to Partner Center as-is for Store ingestion." -ForegroundColor Gray
}
