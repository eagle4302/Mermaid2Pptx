. "$PSScriptRoot/TestSupport.ps1"

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$invokeQa = Join-Path $repo "plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Invoke-Mermaid2PptxQa.ps1"

Assert-True (Test-Path -LiteralPath $invokeQa) "QA adapter exists"

$work = Join-Path $env:TEMP "mermaid2pptx-qa-$([guid]::NewGuid().ToString('N'))"
$qaOut = Join-Path $work "sample"
$unsafeOut = Join-Path $work "existing-output"
New-Item -ItemType Directory -Path $work | Out-Null
New-Item -ItemType Directory -Path $unsafeOut | Out-Null
Set-Content -LiteralPath (Join-Path $unsafeOut "keep.txt") -Value "keep"

try {
    Assert-Throws {
        & $invokeQa `
            -RepoRoot (Join-Path $work "invalid-repository") `
            -Html (Join-Path $work "missing.html") `
            -Out $qaOut
    } "HTML file does not exist"

    Assert-Throws {
        & $invokeQa `
            -RepoRoot (Join-Path $work "invalid-repository") `
            -Html (Join-Path $repo "samples/sample.html") `
            -Out $unsafeOut
    } "refuses to clean"
    Assert-True (
        Test-Path -LiteralPath (Join-Path $unsafeOut "keep.txt") -PathType Leaf
    ) "unsafe output contents preserved"

    $result = & $invokeQa `
        -RepoRoot $repo `
        -Html (Join-Path $repo "samples/sample.html") `
        -Out $qaOut

    Assert-True (
        Test-Path -LiteralPath (Join-Path $qaOut "report.json") -PathType Leaf
    ) "QA report exists"
    Assert-Equal (
        [IO.Path]::GetFullPath($qaOut)
    ) $result "QA output path"
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP)
    Assert-True (
        $resolvedWork.StartsWith(
            $resolvedTemp,
            [StringComparison]::OrdinalIgnoreCase
        )
    ) "QA temporary work path"
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force
}
