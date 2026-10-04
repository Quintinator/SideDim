<#
.SYNOPSIS
    Collects the files for a GitHub release into release/: both exes named with the version, the license,
    SHA256SUMS.txt and release-notes.md (from CHANGELOG.md). Run ./build.ps1 -Version <version> first.

.EXAMPLE
    ./tools/prepare-release.ps1 -Version 0.1.0
#>
param(
    [Parameter(Mandatory)] [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'release'
$publish = Join-Path $root 'artifacts\publish'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null

Copy-Item (Join-Path $publish 'self-contained\SideDim.exe') (Join-Path $out "SideDim-$Version-win-x64.exe")
Copy-Item (Join-Path $publish 'framework-dependent\SideDim.exe') (Join-Path $out "SideDim-$Version-win-x64-needs-dotnet10.exe")
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $out 'LICENSE.txt')

foreach ($exe in Get-ChildItem $out -Filter *.exe) {
    $stamped = $exe.VersionInfo.ProductVersion
    if ($stamped -ne $Version) { throw "$($exe.Name) says version '$stamped', expected '$Version'. Build with ./build.ps1 -Version $Version." }
}

$sums = Get-ChildItem $out -File | Sort-Object Name | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
}
Set-Content -Path (Join-Path $out 'SHA256SUMS.txt') -Value (($sums -join "`n") + "`n") -NoNewline -Encoding Ascii

& (Join-Path $PSScriptRoot 'release-notes.ps1') -Version $Version -Out (Join-Path $root 'release-notes.md')

Get-ChildItem $out | ForEach-Object { Write-Host ("  {0,-45} {1,12:N0} bytes" -f $_.Name, $_.Length) }
