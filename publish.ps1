#requires -version 5
<#
.SYNOPSIS
    Builds the release artifact: a single, self-contained ClaudeTokenTracker.exe.

.DESCRIPTION
    Publishes the WinForms app as ONE portable .exe that bundles the .NET 8 runtime,
    so it runs on any Windows 10 (1803+) / 11 x64 PC with nothing pre-installed.

    Output: release\ClaudeTokenTracker.exe

.EXAMPLE
    .\publish.ps1
#>
[CmdletBinding()]
param(
    # Where the finished .exe is written.
    [string]$OutputDir = "release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# --- Locate dotnet (it isn't always on PATH on this machine) -----------------
function Resolve-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
        (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe")
    )
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    throw "Could not find dotnet. Install the .NET 8 SDK or add dotnet to PATH."
}

$dotnet  = Resolve-Dotnet
$project = Join-Path $root "ClaudeTokenTracker\ClaudeTokenTracker.csproj"
$outAbs  = Join-Path $root $OutputDir

Write-Host "Using dotnet : $dotnet"        -ForegroundColor DarkGray
Write-Host "Publishing   : $project"       -ForegroundColor DarkGray
Write-Host "Output       : $outAbs`n"      -ForegroundColor DarkGray

# Clean prior output so the folder only ever holds the current release.
if (Test-Path $outAbs) { Remove-Item $outAbs -Recurse -Force }

# Publish flags are repeated here on purpose so a partial/misapplied profile cannot
# silently emit a framework-dependent exe that needs .NET 8 on the target PC.
& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishProfile=SingleFile-win-x64 `
    -p:PublishSingleFile=true `
    -p:SelfContained=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=false `
    -p:DebugType=embedded `
    -o $outAbs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$exe = Join-Path $outAbs "ClaudeTokenTracker.exe"
if (-not (Test-Path $exe)) { throw "Publish reported success but $exe is missing." }

$sizeBytes = (Get-Item $exe).Length
$sizeMB = [math]::Round($sizeBytes / 1MB, 1)
# Self-contained single-file bundles the .NET 8 runtime (~60+ MB). A few-hundred-KB exe
# is the framework-dependent apphost from "dotnet build" — it will NOT run without .NET installed.
$minPortableBytes = 40MB
if ($sizeBytes -lt $minPortableBytes) {
    throw @"
Publish produced $exe ($sizeMB MB), which is too small to be a self-contained release.
Expected about 60-70 MB. Do not copy ClaudeTokenTracker.exe from bin\ after 'dotnet build'.
Always distribute the exe from: $outAbs
"@
}
Write-Host "`nDone. Single-file release ready:" -ForegroundColor Green
Write-Host ("  {0}  ({1} MB)" -f $exe, $sizeMB)  -ForegroundColor Green
Write-Host "`nHand that one .exe to anyone on Windows 10/11 x64 - no .NET install needed."
