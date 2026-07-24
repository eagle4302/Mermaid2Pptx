[CmdletBinding()]
param(
    [string]$RepoRoot = $env:MERMAID2PPTX_REPO,
    [string]$DotnetCommand = "dotnet",
    [switch]$ForceRestore,
    [switch]$SkipBrowserInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-Ancestors {
    param([Parameter(Mandatory)][string]$Start)

    $cursor = [IO.Path]::GetFullPath($Start)
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        $cursor
        $parent = Split-Path -Path $cursor -Parent
        if ($parent -eq $cursor) {
            break
        }
        $cursor = $parent
    }
}

function Resolve-Mermaid2PptxRepository {
    param([string]$RequestedRoot)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($RequestedRoot)) {
        $candidates += $RequestedRoot
    }
    else {
        $candidates += @(Get-Ancestors -Start (Get-Location).Path)
        $candidates += @(Get-Ancestors -Start $PSScriptRoot)
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        $full = [IO.Path]::GetFullPath($candidate)
        $solution = Join-Path $full "Mermaid2Pptx.sln"
        $project = Join-Path $full "src/Mermaid2Pptx/Mermaid2Pptx.csproj"
        if ((Test-Path -LiteralPath $solution) -and
            (Test-Path -LiteralPath $project)) {
            return $full
        }
    }

    throw "Could not locate the Mermaid2Pptx repository. Pass -RepoRoot or set MERMAID2PPTX_REPO."
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$FailureMessage
    )

    & $Command @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE."
    }
}

$resolvedRoot = Resolve-Mermaid2PptxRepository -RequestedRoot $RepoRoot
$solutionPath = Join-Path $resolvedRoot "Mermaid2Pptx.sln"
$projectPath = Join-Path $resolvedRoot "src/Mermaid2Pptx/Mermaid2Pptx.csproj"
$qaProjectPath = Join-Path $resolvedRoot "src/Mermaid2Pptx.Qa/Mermaid2Pptx.Qa.csproj"

try {
    $dotnet = Get-Command -Name $DotnetCommand -ErrorAction Stop
}
catch {
    throw "Could not locate dotnet command '$DotnetCommand'. Install the .NET 8 SDK and try again."
}

$dotnetPath = if (-not [string]::IsNullOrWhiteSpace($dotnet.Source)) {
    $dotnet.Source
}
else {
    $DotnetCommand
}

$sdks = @(& $dotnetPath --list-sdks 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "Failed to list installed dotnet SDKs. Exit code: $LASTEXITCODE."
}
if (-not ($sdks | Where-Object { $_ -match "^\s*8\." })) {
    $detected = if ($sdks.Count -gt 0) { $sdks -join "; " } else { "(none)" }
    throw "The .NET 8 SDK is required. Detected SDKs: $detected"
}

Push-Location $resolvedRoot
try {
    $restoreArguments = @("restore", $solutionPath)
    if ($ForceRestore) {
        $restoreArguments += "--force"
    }
    Invoke-NativeCommand `
        -Command $dotnetPath `
        -Arguments $restoreArguments `
        -FailureMessage "dotnet restore failed."

    Invoke-NativeCommand `
        -Command $dotnetPath `
        -Arguments @("build", $solutionPath, "--no-restore") `
        -FailureMessage "dotnet build failed."
}
finally {
    Pop-Location
}

$playwrightScript = Join-Path $resolvedRoot "src/Mermaid2Pptx/bin/Debug/net8.0/playwright.ps1"
if (-not (Test-Path -LiteralPath $playwrightScript)) {
    throw "Build completed without producing the Playwright installer: $playwrightScript"
}

if (-not $SkipBrowserInstall) {
    & $playwrightScript install chromium 2>&1 |
        ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "Playwright Chromium installation failed. Exit code: $LASTEXITCODE."
    }
}

[PSCustomObject]@{
    RepoRoot = $resolvedRoot
    SolutionPath = $solutionPath
    ProjectPath = $projectPath
    QaProjectPath = $qaProjectPath
    PlaywrightScript = $playwrightScript
}
