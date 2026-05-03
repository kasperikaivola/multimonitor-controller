<#
.SYNOPSIS
    Build script for Multi-Monitor Sleep Controller.

.DESCRIPTION
    Builds the application in Debug or Release configuration.
    Supports publishing a self-contained single-file executable.

.PARAMETER Configuration
    Build configuration: Debug or Release. Default is Release.

.PARAMETER Publish
    When set, publishes a self-contained single-file executable to the publish/ directory.

.PARAMETER Clean
    When set, cleans build artifacts before building.

.PARAMETER Runtime
    Target runtime identifier for publishing. Default is win-x64.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Debug
    .\build.ps1 -Publish
    .\build.ps1 -Clean -Publish
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$Publish,

    [switch]$Clean,

    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$ProjectFile = Join-Path $PSScriptRoot "MultiMonitorSleepController\MultiMonitorSleepController.csproj"
$PublishDir   = Join-Path $PSScriptRoot "publish"

function Write-Step($message) {
    Write-Host ""
    Write-Host "=== $message ===" -ForegroundColor Cyan
}

# ── Pre-flight ──────────────────────────────────────────────────────────────

Write-Step "Checking .NET SDK"
try {
    $sdkVersion = dotnet --version
    Write-Host "  .NET SDK $sdkVersion"
} catch {
    Write-Error ".NET SDK not found. Install .NET 9.0 SDK or later from https://dotnet.microsoft.com/download"
    exit 1
}

# ── Clean ───────────────────────────────────────────────────────────────────

if ($Clean) {
    Write-Step "Cleaning"
    dotnet clean $ProjectFile --configuration $Configuration --verbosity quiet
    if (Test-Path $PublishDir) {
        Remove-Item $PublishDir -Recurse -Force
        Write-Host "  Removed publish/"
    }
}

# ── Build ───────────────────────────────────────────────────────────────────

Write-Step "Building ($Configuration)"
dotnet build $ProjectFile --configuration $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit $LASTEXITCODE
}

# ── Publish ─────────────────────────────────────────────────────────────────

if ($Publish) {
    Write-Step "Publishing self-contained single-file ($Runtime)"

    dotnet publish $ProjectFile `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        --output $PublishDir

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed."
        exit $LASTEXITCODE
    }

    $exe = Get-ChildItem $PublishDir -Filter "*.exe" | Select-Object -First 1
    if ($exe) {
        $sizeMb = [math]::Round($exe.Length / 1MB, 2)
        Write-Host ""
        Write-Host "  Published: $($exe.FullName)" -ForegroundColor Green
        Write-Host "  Size:      $sizeMb MB"
    }
}

# ── Done ────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "Build completed successfully." -ForegroundColor Green
