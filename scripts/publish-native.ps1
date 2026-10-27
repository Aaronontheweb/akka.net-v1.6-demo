#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory = $true)]
    [string] $RuntimeIdentifier
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$baseline = Get-Content (Join-Path $PSScriptRoot 'aot-warnings.txt')

foreach ($app in @('Server', 'Client')) {
    $destination = Join-Path $root "artifacts/$($app.ToLowerInvariant())"
    $output = [System.Collections.Generic.List[string]]::new()
    & dotnet publish (Join-Path $root "src/Demo.$app") -c Release -r $RuntimeIdentifier -o $destination 2>&1 |
        ForEach-Object {
            $line = $_.ToString()
            $output.Add($line)
            Write-Host $line
        }
    if ($LASTEXITCODE -ne 0) { throw "Native publish failed for $app" }

    foreach ($line in $output) {
        if ($line -match '(?:warning|error) (IL\d+): (.+?):') {
            $warning = "$($Matches[1]): $($Matches[2])"
            if ($warning -notin $baseline) { throw "New AOT warning: $line" }
        }
        elseif ($line -match '(?:warning|error) IL\d+:') {
            throw "Unrecognized AOT warning: $line"
        }
    }
}
