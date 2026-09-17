[CmdletBinding()]
param(
    [switch]$SkipLaunch,
    [switch]$KeepDownload
)

$ErrorActionPreference = "Stop"

$banner = @"
 ____              _____     _     _
| __ )  ___  _ __ |  ___|   (_)___| |__
|  _ \ / _ \| '_ \| |_ | | | / __| '_ \
| |_) | (_) | | | |  _|| |_| \__ \ | | |
|____/ \___/|_| |_|_|   \__,_|___/_| |_|
        Official CLI Installer
"@

function Write-Step {
    param([string]$Message)

    Write-Host "[>] $Message" -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Message)

    Write-Host "[OK] $Message" -ForegroundColor Green
}

Write-Host $banner -ForegroundColor DarkCyan
Write-Host ""
Write-Host "BoneFish installer for Windows" -ForegroundColor White
Write-Host "https://github.com/tsukiforge/BoneFish" -ForegroundColor DarkGray
Write-Host ""

if ($env:OS -ne "Windows_NT") {
    throw "BoneFish can only be installed on Windows."
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw "BoneFish requires a 64-bit version of Windows."
}

$repository = "tsukiforge/BoneFish"
$apiHeaders = @{ "User-Agent" = "BoneFish-Installer" }

Write-Step "Checking system requirements"
Write-Ok "64-bit Windows detected"

$releaseUri = "https://api.github.com/repos/$repository/releases/latest"

Write-Step "Getting release information"
$release = Invoke-RestMethod -Uri $releaseUri -Headers $apiHeaders
$asset = @($release.assets | Where-Object { $_.name -eq "BoneFish.exe" }) | Select-Object -First 1

if ($null -eq $asset) {
    throw "The selected release does not contain BoneFish.exe."
}

$downloadDirectory = Join-Path ([IO.Path]::GetTempPath()) "BoneFish-install-$([Guid]::NewGuid().ToString('N'))"
$downloadPath = Join-Path $downloadDirectory "BoneFish.exe"
New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null

try {
    Write-Step "Downloading the latest BoneFish release"
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers $apiHeaders -OutFile $downloadPath

    $fileInfo = Get-Item $downloadPath
    if ($fileInfo.Length -lt 1024) {
        throw "The downloaded file is unexpectedly small."
    }

    $signature = [IO.File]::ReadAllBytes($downloadPath)
    if ($signature.Length -lt 2 -or $signature[0] -ne 0x4D -or $signature[1] -ne 0x5A) {
        throw "The downloaded file is not a valid Windows executable."
    }

    Write-Ok "Downloaded $([Math]::Round($fileInfo.Length / 1MB, 2)) MB"

    if ($KeepDownload) {
        $savedPath = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "BoneFish\Downloads\BoneFish-latest.exe"
        New-Item -ItemType Directory -Path (Split-Path $savedPath) -Force | Out-Null
        Copy-Item -Path $downloadPath -Destination $savedPath -Force
        Write-Ok "Saved installer to: $savedPath"
    }

    if (-not $SkipLaunch) {
        Write-Step "Launching the BoneFish setup wizard"
        $process = Start-Process -FilePath $downloadPath -Wait -PassThru
        if ($process.ExitCode -ne 0) {
            throw "BoneFish setup exited with code $($process.ExitCode)."
        }
        Write-Ok "BoneFish setup completed"
    }
    else {
        Write-Ok "Download and validation completed"
        Write-Host "Setup wizard was skipped because -SkipLaunch was used." -ForegroundColor Yellow
    }
}
catch {
    Write-Host "[ERROR] $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "No changes were made by the CLI installer." -ForegroundColor Yellow
    throw
}
finally {
    if (Test-Path $downloadDirectory) {
        Remove-Item -Path $downloadDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}