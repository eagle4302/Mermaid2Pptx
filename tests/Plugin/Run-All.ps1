Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$tests = Get-ChildItem -LiteralPath $PSScriptRoot -Filter "*.Tests.ps1" |
    Sort-Object Name

foreach ($test in $tests) {
    Write-Host "Running $($test.Name)"
    & $test.FullName
}

Write-Host "Plugin tests passed: $($tests.Count)"
