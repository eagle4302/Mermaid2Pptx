. "$PSScriptRoot/TestSupport.ps1"

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$skillRoot = Join-Path $repo "plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations"
$scriptRoot = Join-Path $skillRoot "scripts"
$publisher = Join-Path $scriptRoot "Publish-Mermaid2PptxCandidate.ps1"
$invoke = Join-Path $scriptRoot "Invoke-Mermaid2Pptx.ps1"

$publicScripts = @(
    Get-ChildItem -LiteralPath $scriptRoot -Filter "*.ps1" -File
)
foreach ($script in $publicScripts) {
    $firstLine = Get-Content -LiteralPath $script.FullName -TotalCount 1
    Assert-Equal "#requires -Version 5.1" $firstLine "PowerShell preflight: $($script.Name)"
}

$windowsPowerShell = Get-Command "powershell.exe" -ErrorAction SilentlyContinue
if ($null -eq $windowsPowerShell) {
    Write-Host "Skipping Windows PowerShell 5.1 runtime checks on this platform."
    return
}

$work = Join-Path $env:TEMP "mermaid2pptx-ps51-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null

try {
    $candidate = Join-Path $work "candidate.pptx"
    $published = Join-Path $work "published.pptx"
    [IO.File]::WriteAllText($candidate, "candidate")

    $publisherOutput = @(
        & $windowsPowerShell.Source `
            -NoProfile `
            -NonInteractive `
            -ExecutionPolicy Bypass `
            -File $publisher `
            -Candidate $candidate `
            -Destination $published 2>&1
    )
    Assert-Equal 0 $LASTEXITCODE "PowerShell 5.1 publisher exit code: $($publisherOutput -join ' | ')"
    Assert-Equal "candidate" (
        [IO.File]::ReadAllText($published)
    ) "PowerShell 5.1 publisher output"

    $deck = Join-Path $work "ps51-wrapper.pptx"
    $wrapperOutput = @(
        & $windowsPowerShell.Source `
            -NoProfile `
            -NonInteractive `
            -ExecutionPolicy Bypass `
            -File $invoke `
            -RepoRoot $repo `
            -Mermaid "flowchart LR; A-->B" `
            -Out $deck `
            -SkipBrowserInstall 2>&1
    )
    Assert-Equal 0 $LASTEXITCODE "PowerShell 5.1 wrapper exit code: $($wrapperOutput -join ' | ')"
    Assert-True (Test-Path -LiteralPath $deck) "PowerShell 5.1 wrapper output"
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP)
    Assert-True (
        $resolvedWork.StartsWith(
            $resolvedTemp,
            [StringComparison]::OrdinalIgnoreCase
        )
    ) "PowerShell compatibility temporary work path"
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force
}
