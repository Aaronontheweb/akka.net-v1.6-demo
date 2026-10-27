#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory = $true)]
    [string] $RuntimeIdentifier,
    [string] $ArtifactsPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts')
)

$ErrorActionPreference = 'Stop'
$suffix = if ($RuntimeIdentifier.StartsWith('win-')) { '.exe' } else { '' }
$report = @('| Runtime | Application | Bytes | MiB |', '| --- | --- | ---: | ---: |')

foreach ($app in @('Server', 'Client')) {
    $binary = Get-Item (Join-Path $ArtifactsPath "$($app.ToLowerInvariant())/Demo.$app$suffix")
    $mib = ($binary.Length / 1MB).ToString('F2', [System.Globalization.CultureInfo]::InvariantCulture)
    $report += "| $RuntimeIdentifier | $app | $($binary.Length) | $mib |"
}

$report | Set-Content (Join-Path $ArtifactsPath 'native-sizes.md')
$report | Write-Output
if ($env:GITHUB_STEP_SUMMARY) { $report | Add-Content $env:GITHUB_STEP_SUMMARY }
