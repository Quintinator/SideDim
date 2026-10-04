<#
.SYNOPSIS
    The whole pipeline: restore, format check, build, test, publish.
    CI runs exactly this script, so a green local run means a green CI run.

.PARAMETER Version
    Version stamped into the exe, e.g. 0.2.0.

.EXAMPLE
    ./build.ps1                 # everything
    ./build.ps1 -SkipPublish    # quick check before committing
#>
param(
    [switch]$SkipPublish,
    [string]$Configuration = 'Release',
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$artifacts = Join-Path $PSScriptRoot 'artifacts'

function Step([string]$Name, [scriptblock]$Command) {
    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit code $LASTEXITCODE)" }
    Write-Host "    $Name OK in $([math]::Round($sw.Elapsed.TotalSeconds, 1))s" -ForegroundColor DarkGray
}

Step 'Restore' { dotnet restore }
Step 'Format check' { dotnet format --verify-no-changes --no-restore }
Step 'Build' { dotnet build --configuration $Configuration --no-restore -warnaserror }
Step 'Test' {
    dotnet test --solution SideDim.sln --configuration $Configuration --no-build `
        --timeout 2m --minimum-expected-tests 100 `
        --results-directory (Join-Path $artifacts 'test-results') `
        --report-xunit-trx --report-xunit-trx-filename test-results.trx
}

if (-not $SkipPublish) {
    $common = @('src', '--configuration', 'Release', '--runtime', 'win-x64',
                '-p:PublishSingleFile=true', '-p:DebugType=none')
    if ($Version) { $common += "-p:Version=$Version" }
    Step 'Publish self-contained exe' {
        dotnet publish @common --self-contained true `
            -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
            --output (Join-Path $artifacts 'publish\self-contained')
    }
    Step 'Publish framework-dependent exe' {
        dotnet publish @common --self-contained false `
            --output (Join-Path $artifacts 'publish\framework-dependent')
    }
}

Write-Host ""
Write-Host "Pipeline passed." -ForegroundColor Green
