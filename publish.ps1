<#
.SYNOPSIS
    Build and package SwaggerToMarkdownTool for distribution.
.PARAMETER Runtime
    Target runtime identifier(s). Defaults to current platform.
    Examples: win-x64, linux-x64, osx-x64, osx-arm64
    Use "all" to build for all supported platforms.
.PARAMETER Output
    Output directory for packaged artifacts. Defaults to ./artifacts
.PARAMETER SkipClean
    Skip cleaning previous build artifacts.
#>
param(
    [string[]]$Runtime,
    [string]$Output = "artifacts",
    [switch]$SkipClean
)

$ErrorActionPreference = "Stop"

$ProjectDir = "SwaggerToMarkdownTool"
$ProjectName = "SwaggerToMarkdownTool"

$AllRuntimes = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")

if (-not $Runtime -or $Runtime -contains "all") {
    $Runtime = $AllRuntimes
}

if (-not $SkipClean -and (Test-Path $Output)) {
    Write-Host "Cleaning $Output ..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $Output
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$OutputFull = (Resolve-Path $Output).Path

# Read version from csproj or default to 1.0.0
$csproj = [xml](Get-Content "$ProjectDir/$ProjectName.csproj")
$version = $csproj.Project.PropertyGroup.Version
if (-not $version) { $version = "1.0.0" }

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  $ProjectName v$version" -ForegroundColor Cyan
Write-Host "  Targets: $($Runtime -join ', ')" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$results = @()

foreach ($rid in $Runtime) {
    Write-Host "[$rid] Publishing ..." -ForegroundColor Green

    $publishDir = Join-Path $OutputFull "publish/$rid"

    dotnet publish $ProjectDir `
        -c Release `
        -r $rid `
        --self-contained true `
        -o $publishDir `
        2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[$rid] Publish FAILED" -ForegroundColor Red
        $results += [PSCustomObject]@{ Runtime = $rid; Status = "FAILED"; File = "-" }
        continue
    }

    # Determine executable name
    $exeName = if ($rid.StartsWith("win")) { "$ProjectName.exe" } else { $ProjectName }
    $exePath = Join-Path $publishDir $exeName

    if (-not (Test-Path $exePath)) {
        Write-Host "[$rid] Executable not found: $exePath" -ForegroundColor Red
        $results += [PSCustomObject]@{ Runtime = $rid; Status = "FAILED"; File = "-" }
        continue
    }

    # Package
    $packageName = "$ProjectName-v$version-$rid"

    if ($rid.StartsWith("win")) {
        $zipFile = "$packageName.zip"
        $zipPath = Join-Path $OutputFull $zipFile
        Compress-Archive -Path $exePath -DestinationPath $zipPath -Force
        $size = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
        Write-Host "[$rid] Packaged: $zipFile ($size MB)" -ForegroundColor Green
        $results += [PSCustomObject]@{ Runtime = $rid; Status = "OK"; File = $zipFile }
    }
    else {
        $tarFile = "$packageName.tar.gz"
        $tarPath = Join-Path $OutputFull $tarFile
        tar -czf $tarPath -C $publishDir $exeName 2>$null
        if ($LASTEXITCODE -ne 0) {
            # Fallback: just copy the binary
            Copy-Item $exePath (Join-Path $OutputFull $exeName)
            Write-Host "[$rid] tar not available, copied binary directly" -ForegroundColor Yellow
            $results += [PSCustomObject]@{ Runtime = $rid; Status = "OK (binary)"; File = $exeName }
        }
        else {
            $size = [math]::Round((Get-Item $tarPath).Length / 1MB, 2)
            Write-Host "[$rid] Packaged: $tarFile ($size MB)" -ForegroundColor Green
            $results += [PSCustomObject]@{ Runtime = $rid; Status = "OK"; File = $tarFile }
        }
    }
}

# Cleanup intermediate publish directories
if (Test-Path (Join-Path $OutputFull "publish")) {
    Remove-Item -Recurse -Force (Join-Path $OutputFull "publish")
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
$results | Format-Table -AutoSize
Write-Host "Artifacts directory: $OutputFull"
