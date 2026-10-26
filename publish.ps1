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
$version = $csproj.SelectSingleNode("//Version").InnerText
if (-not $version) { $version = "1.0.0" }

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  $ProjectName v$version" -ForegroundColor Cyan
Write-Host "  Targets: $($Runtime -join ', ')" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$results = @()

function New-PosixTarGz {
    param(
        [Parameter(Mandatory = $true)][string]$SourceFile,
        [Parameter(Mandatory = $true)][string]$EntryName,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    # Windows tar cannot store a Unix executable bit. PowerShell 7 can.
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) {
        $packer = @'
$ErrorActionPreference = "Stop"
$src = $env:PKG_SRC
$name = $env:PKG_NAME
$dest = $env:PKG_DEST
if (Test-Path $dest) { Remove-Item -Force $dest }
$out = [System.IO.File]::Create($dest)
try {
    $gzip = New-Object System.IO.Compression.GZipStream($out, [System.IO.Compression.CompressionLevel]::Optimal)
    try {
        $writer = [System.Formats.Tar.TarWriter]::new($gzip, [System.Formats.Tar.TarEntryFormat]::Pax, $true)
        try {
            $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, $name)
            $entry.Mode = [enum]::ToObject([System.IO.UnixFileMode], 493)
            $data = [System.IO.File]::OpenRead($src)
            try {
                $entry.DataStream = $data
                $writer.WriteEntry($entry)
            } finally { $data.Dispose() }
        } finally { $writer.Dispose() }
    } finally { $gzip.Dispose() }
} finally { $out.Dispose() }
'@
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($packer))
        $env:PKG_SRC = $SourceFile
        $env:PKG_NAME = $EntryName
        $env:PKG_DEST = $Destination
        $prevErrorAction = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        & pwsh -NoProfile -EncodedCommand $encoded
        $pwshExit = $LASTEXITCODE
        $ErrorActionPreference = $prevErrorAction
        Remove-Item Env:PKG_SRC, Env:PKG_NAME, Env:PKG_DEST -ErrorAction SilentlyContinue
        if ($pwshExit -eq 0 -and (Test-Path $Destination)) {
            return $true
        }
    }

    $prevErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & tar -czf $Destination -C (Split-Path -Parent $SourceFile) $EntryName 2>&1 | Out-Null
    $tarExit = $LASTEXITCODE
    $ErrorActionPreference = $prevErrorAction
    return ($tarExit -eq 0 -and (Test-Path $Destination))
}

foreach ($rid in $Runtime) {
    Write-Host "[$rid] Publishing ..." -ForegroundColor Green

    $publishDir = Join-Path $OutputFull "publish/$rid"

    $prevErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & dotnet publish $ProjectDir `
        -c Release `
        -r $rid `
        --self-contained true `
        -o $publishDir 2>&1 | ForEach-Object { Write-Host $_ }
    $publishExit = $LASTEXITCODE
    $ErrorActionPreference = $prevErrorAction

    if ($publishExit -ne 0) {
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
        $packed = New-PosixTarGz -SourceFile $exePath -EntryName $exeName -Destination $tarPath
        if (-not $packed) {
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
