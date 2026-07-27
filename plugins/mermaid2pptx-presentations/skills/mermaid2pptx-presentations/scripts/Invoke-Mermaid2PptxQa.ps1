#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Html,
    [Parameter(Mandatory)][string]$Out,
    [string]$RepoRoot = $env:MERMAID2PPTX_REPO,
    [string]$SlideSelector = ".slide",
    [string]$SvgSelector = "svg",
    [double]$Width = 13.333,
    [double]$Height = 7.5,
    [switch]$SkipBrowserInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$htmlPath = [IO.Path]::GetFullPath($Html)
if (-not (Test-Path -LiteralPath $htmlPath -PathType Leaf)) {
    throw "HTML file does not exist: $htmlPath"
}

$outputPath = [IO.Path]::GetFullPath($Out)
if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
    throw "QA output path is a file: $outputPath"
}

$outputParent = Split-Path -Path $outputPath -Parent
if ([string]::IsNullOrWhiteSpace($outputParent)) {
    throw "QA output cannot be a filesystem root: $outputPath"
}
if ($outputPath.Equals(
    (Split-Path -Path $htmlPath -Parent),
    [StringComparison]::OrdinalIgnoreCase
)) {
    throw "QA output cannot be the HTML source directory: $outputPath"
}

if (Test-Path -LiteralPath $outputPath -PathType Container) {
    $existingEntries = @(
        Get-ChildItem -LiteralPath $outputPath -Force
    )
    $existingReport = Join-Path $outputPath "report.json"
    if ($existingEntries.Count -gt 0 -and
        -not (Test-Path -LiteralPath $existingReport -PathType Leaf)) {
        throw "QA refuses to clean a non-empty directory that is not an existing Mermaid2Pptx QA output: $outputPath"
    }
}

$initializer = Join-Path $PSScriptRoot "Initialize-Mermaid2Pptx.ps1"
$environment = & $initializer `
    -RepoRoot $RepoRoot `
    -SkipBrowserInstall:$SkipBrowserInstall
if ($outputPath.Equals(
    $environment.RepoRoot,
    [StringComparison]::OrdinalIgnoreCase
)) {
    throw "QA output cannot be the Mermaid2Pptx repository root."
}

$arguments = @(
    "--html", $htmlPath,
    "--out", $outputPath,
    "--slide-selector", $SlideSelector,
    "--svg-selector", $SvgSelector,
    "--width", $Width.ToString(
        [Globalization.CultureInfo]::InvariantCulture
    ),
    "--height", $Height.ToString(
        [Globalization.CultureInfo]::InvariantCulture
    )
)

$commandOutput = @(
    & $environment.QaCliPath @arguments 2>&1
)
$qaExitCode = $LASTEXITCODE
$commandOutput | ForEach-Object { Write-Host $_ }
if ($qaExitCode -ne 0) {
    throw "Mermaid2Pptx QA failed. $($commandOutput -join [Environment]::NewLine)"
}

$reportPath = Join-Path $outputPath "report.json"
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw "Mermaid2Pptx QA reported success without writing report.json: $reportPath"
}

$outputPath
