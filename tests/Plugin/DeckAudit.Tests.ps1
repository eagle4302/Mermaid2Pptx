. "$PSScriptRoot/TestSupport.ps1"

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$audit = Join-Path $repo "plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Test-Mermaid2PptxDeck.ps1"
$project = Join-Path $repo "src/Mermaid2Pptx/Mermaid2Pptx.csproj"

Assert-True (Test-Path -LiteralPath $audit) "deck audit exists"

$work = Join-Path $env:TEMP "mermaid2pptx-audit-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null

$standalone = Join-Path $work "standalone.pptx"
$inserted = Join-Path $work "inserted.pptx"
$tampered = Join-Path $work "tampered.pptx"
$invalidMermaid = Join-Path $work "invalid-mermaid.pptx"

try {
    & dotnet run --no-build --project $project -- `
        --mermaid "graph TD; A-->B" `
        --out $standalone
    Assert-Equal 0 $LASTEXITCODE "standalone conversion exit code"

    $standaloneResult = @(& $audit -Pptx $standalone -Standalone)
    Assert-Equal 1 $standaloneResult.Count "standalone audit result count"
    Assert-True ($standaloneResult[0].ShapeCount -gt 0) "standalone native shape count"

    & dotnet run --no-build --project $project -- `
        --mermaid "graph LR; C-->D" `
        --insert-into $standalone `
        --map "1=1" `
        --out $inserted
    Assert-Equal 0 $LASTEXITCODE "insertion conversion exit code"

    $insertedResult = @(
        & $audit `
            -Pptx $inserted `
            -BaselinePptx $standalone `
            -DiagramSlides 1
    )
    Assert-Equal 1 $insertedResult.Count "insertion audit result count"
    Assert-True (
        $insertedResult[0].ShapeCount -gt $insertedResult[0].BaselineShapeCount
    ) "insertion adds native shapes"
    Assert-Equal (
        $insertedResult[0].BaselinePictureCount
    ) $insertedResult[0].PictureCount "insertion picture count"
    Assert-Equal (
        $insertedResult[0].BaselineImageRelationshipCount
    ) $insertedResult[0].ImageRelationshipCount "insertion image relationships"

    & dotnet run --no-build --project $project -- `
        --mermaid "this is not valid Mermaid syntax" `
        --out $invalidMermaid
    Assert-Equal 0 $LASTEXITCODE "invalid Mermaid renderer exit code"
    Assert-Throws {
        & $audit -Pptx $invalidMermaid -Standalone
    } "Mermaid render error"

    Copy-Item -LiteralPath $standalone -Destination $tampered
    $archive = [IO.Compression.ZipFile]::Open(
        $tampered,
        [IO.Compression.ZipArchiveMode]::Update
    )
    try {
        $entry = $archive.CreateEntry("ppt/media/fake.png")
        $stream = $entry.Open()
        try {
            $stream.WriteByte(0)
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    Assert-Throws {
        & $audit -Pptx $tampered -Standalone
    } "media"
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP)
    Assert-True (
        $resolvedWork.StartsWith(
            $resolvedTemp,
            [StringComparison]::OrdinalIgnoreCase
        )
    ) "audit temporary work path"
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force
}
