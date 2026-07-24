. "$PSScriptRoot/TestSupport.ps1"

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$invoke = Join-Path $repo "plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Invoke-Mermaid2Pptx.ps1"

Assert-True (Test-Path -LiteralPath $invoke) "invocation adapter exists"

$work = Join-Path $env:TEMP "mermaid2pptx-invocation-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null

$standalone = Join-Path $work "standalone.pptx"
$fromFile = Join-Path $work "from-file.pptx"
$fromStdin = Join-Path $work "from-stdin.pptx"
$fromHtml = Join-Path $work "from-html.pptx"
$inserted = Join-Path $work "inserted.pptx"
$fromSourcePptx = Join-Path $work "from-source-pptx.pptx"
$badMap = Join-Path $work "bad-map.pptx"
$invalidMermaid = Join-Path $work "invalid-mermaid.pptx"

try {
    & $invoke `
        -RepoRoot $repo `
        -Mermaid "graph TD; A-->B" `
        -Out $standalone
    Assert-True (Test-Path -LiteralPath $standalone) "standalone output"

    $mmd = Join-Path $work "diagram.mmd"
    Set-Content -LiteralPath $mmd -Value "graph LR; FileA-->FileB"
    & $invoke -RepoRoot $repo -MermaidFile $mmd -Out $fromFile
    "graph TD; StdinA-->StdinB" |
        & $invoke -RepoRoot $repo -MermaidStdin -Out $fromStdin
    & $invoke `
        -RepoRoot $repo `
        -Html (Join-Path $repo "samples/sample.html") `
        -Out $fromHtml
    Assert-True (Test-Path -LiteralPath $fromFile) "Mermaid file output"
    Assert-True (Test-Path -LiteralPath $fromStdin) "Mermaid stdin output"
    Assert-True (Test-Path -LiteralPath $fromHtml) "HTML output"

    $baseHash = (Get-FileHash $standalone -Algorithm SHA256).Hash
    & $invoke `
        -RepoRoot $repo `
        -Mermaid "graph LR; C-->D" `
        -InsertInto $standalone `
        -Map "1=1" `
        -Out $inserted
    Assert-Equal (
        $baseHash
    ) (Get-FileHash $standalone -Algorithm SHA256).Hash "source preserved"

    & $invoke `
        -RepoRoot $repo `
        -SourcePptx $fromFile `
        -InsertInto $standalone `
        -Map "1=1" `
        -Out $fromSourcePptx
    Assert-True (
        Test-Path -LiteralPath $fromSourcePptx
    ) "source PPTX insertion"

    Assert-Throws {
        & $invoke `
            -RepoRoot $repo `
            -Mermaid "graph TD; X-->Y" `
            -Out $standalone
    } "already exists"

    Assert-Throws {
        & $invoke `
            -RepoRoot $repo `
            -Mermaid "graph TD; X-->Y" `
            -InsertInto $standalone `
            -Map "99=1" `
            -Out $badMap
    } "slide|range"
    Assert-True (
        -not (Test-Path -LiteralPath $badMap)
    ) "invalid mapping publishes nothing"

    Assert-Throws {
        & $invoke `
            -RepoRoot $repo `
            -Mermaid "this is not valid Mermaid syntax" `
            -Out $invalidMermaid
    } "Mermaid|convert|render"
    Assert-True (
        -not (Test-Path -LiteralPath $invalidMermaid)
    ) "invalid Mermaid publishes nothing"
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP)
    Assert-True (
        $resolvedWork.StartsWith(
            $resolvedTemp,
            [StringComparison]::OrdinalIgnoreCase
        )
    ) "invocation temporary work path"
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force
}
