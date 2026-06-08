param(
    [string]$Version = "1.0.0",
    [switch]$SkipVelopack,
    [switch]$SkipPackage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root   = $PSScriptRoot
$dist   = Join-Path $root "dist"

function Step([string]$msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}
function Die([string]$msg) {
    Write-Host "[FAIL] $msg" -ForegroundColor Red
    exit 1
}
function Ok([string]$msg) {
    Write-Host "  $msg" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# git SHA
# ---------------------------------------------------------------------------
$sha = git rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sha)) { Die "Not in a git repo or git not found." }
$shaShort = $sha.Substring(0, 7)
$tag      = "$Version-$shaShort"

Step "Production build  v$Version  ($shaShort)"
Write-Host "  InformationalVersion: $Version+$sha" -ForegroundColor DarkGray

# ---------------------------------------------------------------------------
# clean dist
# ---------------------------------------------------------------------------
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item $dist -ItemType Directory | Out-Null

# ---------------------------------------------------------------------------
# DepotDownloaderMod  (managed .NET DLL — build once, copy to all targets)
# ---------------------------------------------------------------------------
Step "Building DepotDownloaderMod"
Push-Location (Join-Path $root "DepotDownloaderMod")
try {
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) { Die "DepotDownloaderMod build failed." }
} finally { Pop-Location }

$ddmodSrc = Join-Path $root "DepotDownloaderMod\bin\Release\net9.0"

# ---------------------------------------------------------------------------
# helper: publish + copy ddmod
# ---------------------------------------------------------------------------
function Publish-Target {
    param(
        [string]$Project,      # e.g. "DepotDL.CLI"
        [string]$Rid,          # e.g. "linux-x64"
        [string]$Framework     # e.g. "net9.0" or "net9.0-windows"
    )

    Step "Publishing $Project  [$Rid]"
    Push-Location (Join-Path $root $Project)
    try {
        dotnet publish `
            -c Release `
            -r $Rid `
            --self-contained true `
            /p:PublishSingleFile=true `
            /p:Version=$Version `
            /p:SourceRevisionId=$sha
        if ($LASTEXITCODE -ne 0) { Die "$Project [$Rid] publish failed." }
    } finally { Pop-Location }

    $publishDir = Join-Path $root "$Project\bin\Release\$Framework\$Rid\publish"

    # copy ddmod files (skip .exe — Windows-only host binary not useful on other targets)
    Get-ChildItem $ddmodSrc -File | Where-Object { $_.Extension -ne ".exe" } |
        ForEach-Object { Copy-Item $_.FullName $publishDir -Force }

    return $publishDir
}

# ---------------------------------------------------------------------------
# CLI — all three platforms
# ---------------------------------------------------------------------------
$cliWin   = Publish-Target "DepotDL.CLI" "win-x64"   "net9.0"
$cliLinux = Publish-Target "DepotDL.CLI" "linux-x64"  "net9.0"
$cliMac   = Publish-Target "DepotDL.CLI" "osx-arm64"  "net9.0"

# ---------------------------------------------------------------------------
# GUI — Windows only (WPF)
# ---------------------------------------------------------------------------
Step "Publishing DepotDL.GUI  [win-x64]"
Push-Location (Join-Path $root "DepotDL.GUI")
try {
    dotnet publish `
        -c Release `
        -r win-x64 `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:Version=$Version `
        /p:SourceRevisionId=$sha
    if ($LASTEXITCODE -ne 0) { Die "GUI publish failed." }
} finally { Pop-Location }

$guiWin = Join-Path $root "DepotDL.GUI\bin\Release\net9.0-windows\win-x64\publish"
Get-ChildItem $ddmodSrc -File | Where-Object { $_.Extension -ne ".exe" } |
    ForEach-Object { Copy-Item $_.FullName $guiWin -Force }

# ---------------------------------------------------------------------------
# Velopack  (Windows GUI installer / updater)
# ---------------------------------------------------------------------------
if (-not $SkipVelopack) {
    Step "Packing GUI with Velopack  (v$Version)"
    $vpkOut = Join-Path $dist "setup"
    New-Item $vpkOut -ItemType Directory | Out-Null

    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
        Write-Host "  vpk not found — installing..." -ForegroundColor Yellow
        dotnet tool install -g vpk --version 0.0.1298
        if ($LASTEXITCODE -ne 0) { Die "vpk install failed." }
    }

    vpk pack `
        --packId      DepotDL `
        --packVersion $Version `
        --packDir     $guiWin `
        --mainExe     DepotDL.GUI.exe `
        --packTitle   "DepotDL" `
        --outputDir   $vpkOut

    if ($LASTEXITCODE -ne 0) { Die "vpk pack failed." }
    Ok "Setup files → dist\setup\"
}

# ---------------------------------------------------------------------------
# ZIP packages
# ---------------------------------------------------------------------------
if (-not $SkipPackage) {
    Step "Creating ZIP packages"

    $zips = @(
        @{ Src = $cliWin;   Name = "DepotDL.CLI-Windows-x64-$tag.zip"  },
        @{ Src = $cliLinux; Name = "DepotDL.CLI-Linux-x64-$tag.zip"    },
        @{ Src = $cliMac;   Name = "DepotDL.CLI-macOS-arm64-$tag.zip"  },
        @{ Src = $guiWin;   Name = "DepotDL.GUI-Windows-x64-$tag.zip"  }
    )

    foreach ($z in $zips) {
        $dest = Join-Path $dist $z.Name
        Compress-Archive -Path "$($z.Src)\*" -DestinationPath $dest -Force
        Ok $z.Name
    }
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Step "Done"
Write-Host "  Version : $Version" -ForegroundColor White
Write-Host "  SHA     : $shaShort" -ForegroundColor White
Write-Host "  Output  : $dist" -ForegroundColor White
Write-Host ""
Get-ChildItem $dist | ForEach-Object {
    Write-Host "    $($_.Name)" -ForegroundColor DarkGray
}
