<#
.SYNOPSIS
    Writes the CHANGELOG.md section for one version to a file, for the GitHub release notes.
    Fails when the changelog has no section for that version, so a release can't go out without notes.

.EXAMPLE
    ./tools/release-notes.ps1 -Version 0.1.0 -Out release-notes.md
#>
param(
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$Out
)

$ErrorActionPreference = 'Stop'
$changelog = Get-Content (Join-Path $PSScriptRoot '..\CHANGELOG.md') -Raw
$pattern = '(?ms)^## \[' + [regex]::Escape($Version) + '\][^\r\n]*\r?\n(.*?)(?=^## \[|\z)'
$match = [regex]::Match($changelog, $pattern)
if (-not $match.Success -or -not $match.Groups[1].Value.Trim()) {
    throw "CHANGELOG.md has no notes for version $Version. Add a '## [$Version] - <date>' section first."
}
Set-Content -Path $Out -Value $match.Groups[1].Value.Trim() -Encoding UTF8
Write-Host "Release notes for $Version written to $Out"
