. "$PSScriptRoot/TestSupport.ps1"

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$publish = Join-Path $repo "plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Publish-Mermaid2PptxCandidate.ps1"

Assert-True (Test-Path -LiteralPath $publish) "atomic publisher exists"

$work = Join-Path $env:TEMP "mermaid2pptx-publication-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null

try {
    $newCandidate = Join-Path $work "new-candidate.pptx"
    $newOutput = Join-Path $work "new-output.pptx"
    [IO.File]::WriteAllText($newCandidate, "new")

    $published = & $publish `
        -Candidate $newCandidate `
        -Destination $newOutput
    Assert-Equal (
        [IO.Path]::GetFullPath($newOutput)
    ) $published "new output path"
    Assert-Equal "new" (
        [IO.File]::ReadAllText($newOutput)
    ) "new output content"
    Assert-True (
        -not (Test-Path -LiteralPath $newCandidate)
    ) "new candidate moved"

    $collisionCandidate = Join-Path $work "collision-candidate.pptx"
    $collisionOutput = Join-Path $work "collision-output.pptx"
    [IO.File]::WriteAllText($collisionCandidate, "candidate")
    [IO.File]::WriteAllText($collisionOutput, "existing")

    Assert-Throws {
        & $publish `
            -Candidate $collisionCandidate `
            -Destination $collisionOutput
    } "already exists|without -Force"
    Assert-Equal "existing" (
        [IO.File]::ReadAllText($collisionOutput)
    ) "collision preserves destination"
    Assert-Equal "candidate" (
        [IO.File]::ReadAllText($collisionCandidate)
    ) "collision preserves candidate"

    $replaceCandidate = Join-Path $work "replace-candidate.pptx"
    $replaceOutput = Join-Path $work "replace-output.pptx"
    [IO.File]::WriteAllText($replaceCandidate, "replacement")
    [IO.File]::WriteAllText($replaceOutput, "original")

    & $publish `
        -Candidate $replaceCandidate `
        -Destination $replaceOutput `
        -Force | Out-Null
    Assert-Equal "replacement" (
        [IO.File]::ReadAllText($replaceOutput)
    ) "forced replacement content"
    Assert-True (
        -not (Test-Path -LiteralPath $replaceCandidate)
    ) "replacement candidate consumed"
    Assert-Equal 0 @(
        Get-ChildItem -LiteralPath $work -Filter "*.rollback"
    ).Count "rollback backup removed"

    $lockedCandidate = Join-Path $work "locked-candidate.pptx"
    $lockedOutput = Join-Path $work "locked-output.pptx"
    [IO.File]::WriteAllText($lockedCandidate, "locked replacement")
    [IO.File]::WriteAllText($lockedOutput, "locked original")
    $lockedStream = [IO.File]::Open(
        $lockedOutput,
        [IO.FileMode]::Open,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::None
    )
    try {
        Assert-Throws {
            & $publish `
                -Candidate $lockedCandidate `
                -Destination $lockedOutput `
                -Force
        } "atomically replace|publish"
    }
    finally {
        $lockedStream.Dispose()
    }
    Assert-Equal "locked original" (
        [IO.File]::ReadAllText($lockedOutput)
    ) "failed replacement preserves destination"
    Assert-Equal "locked replacement" (
        [IO.File]::ReadAllText($lockedCandidate)
    ) "failed replacement preserves candidate"
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP)
    Assert-True (
        $resolvedWork.StartsWith(
            $resolvedTemp,
            [StringComparison]::OrdinalIgnoreCase
        )
    ) "publication temporary work path"
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force
}
