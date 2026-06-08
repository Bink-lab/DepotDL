param(
    [string]$Version = "1.0.0",
    [switch]$SkipVelopack,
    [switch]$SkipPackage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$dist = Join-Path $root "dist"

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

$sha = git rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sha)) { Die "Not in a git repo or git not found." }
$shaShort = $sha.Substring(0, 7)
$tag      = "$Version-$shaShort"

Step "Production build  v$Version  ($shaShort)"
Write-Host "  InformationalVersion: $Version+$sha" -ForegroundColor DarkGray

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item $dist -ItemType Directory | Out-Null

Step "Building DepotDownloaderMod"
Push-Location (Join-Path $root "DepotDownloaderMod")
try {
    dotnet build -c Release | Out-Host
    if ($LASTEXITCODE -ne 0) { Die "DepotDownloaderMod build failed." }
} finally { Pop-Location }

$ddmodSrc = Join-Path $root "DepotDownloaderMod\bin\Release\net9.0"

function Invoke-Publish([string]$Project, [string]$Rid, [string]$Framework) {
    Step "Publishing $Project  [$Rid]"
    Push-Location (Join-Path $root $Project)
    try {
        dotnet publish `
            -c Release `
            -r $Rid `
            --self-contained true `
            /p:PublishSingleFile=true `
            /p:Version=$Version `
            /p:SourceRevisionId=$sha | Out-Host
        if ($LASTEXITCODE -ne 0) { Die "$Project [$Rid] publish failed." }
    } finally { Pop-Location }

    $dir = Join-Path $root "$Project\bin\Release\$Framework\$Rid\publish"
    Get-ChildItem $ddmodSrc -File | Where-Object { $_.Extension -ne ".exe" } |
        ForEach-Object { Copy-Item $_.FullName $dir -Force }
    Write-Output $dir
}

$cliWin   = Invoke-Publish "DepotDL.CLI" "win-x64"  "net9.0"
$cliLinux = Invoke-Publish "DepotDL.CLI" "linux-x64" "net9.0"
$cliMac   = Invoke-Publish "DepotDL.CLI" "osx-arm64" "net9.0"

$guiWin = Invoke-Publish "DepotDL.GUI" "win-x64" "net9.0-windows"

if (-not $SkipVelopack) {
    Step "Packing GUI with Velopack  (v$Version)"
    $vpkOut = Join-Path $dist "setup"
    New-Item $vpkOut -ItemType Directory | Out-Null

    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
        Write-Host "  vpk not found — installing..." -ForegroundColor Yellow
        dotnet tool install -g vpk | Out-Host
        if ($LASTEXITCODE -ne 0) { Die "vpk install failed." }
    } else {
        dotnet tool update -g vpk | Out-Host
    }

    vpk pack `
        --packId      DepotDL `
        --packVersion $Version `
        --packDir     $guiWin `
        --mainExe     DepotDL.GUI.exe `
        --packTitle   "DepotDL" `
        --outputDir   $vpkOut | Out-Host

    if ($LASTEXITCODE -ne 0) { Die "vpk pack failed." }
    Ok "Setup files → dist\setup\"
}

if (-not $SkipPackage) {
    Step "Creating ZIP packages"

    $zips = @(
        @{ Src = $cliWin;   Name = "DepotDL.CLI-Windows-x64-$tag.zip"  }
        @{ Src = $cliLinux; Name = "DepotDL.CLI-Linux-x64-$tag.zip"    }
        @{ Src = $cliMac;   Name = "DepotDL.CLI-macOS-arm64-$tag.zip"  }
        @{ Src = $guiWin;   Name = "DepotDL.GUI-Windows-x64-$tag.zip"  }
    )

    foreach ($z in $zips) {
        $dest = Join-Path $dist $z.Name
        Compress-Archive -Path "$($z.Src)\*" -DestinationPath $dest -Force
        Ok $z.Name
    }
}

Step "Done"
Write-Host "  Version : $Version" -ForegroundColor White
Write-Host "  SHA     : $shaShort" -ForegroundColor White
Write-Host "  Output  : $dist" -ForegroundColor White
Write-Host ""
Get-ChildItem $dist | ForEach-Object { Write-Host "    $($_.Name)" -ForegroundColor DarkGray }
