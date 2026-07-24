. "$PSScriptRoot/TestSupport.ps1"

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scripts = Join-Path $repo "plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts"
$invoke = Join-Path $scripts "Invoke-Mermaid2Pptx.ps1"
$audit = Join-Path $scripts "Test-Mermaid2PptxDeck.ps1"
$project = Join-Path $repo "src/Mermaid2Pptx/Mermaid2Pptx.csproj"

function Get-ZipEntryHash {
    param(
        [Parameter(Mandatory)][string]$Pptx,
        [Parameter(Mandatory)][string]$EntryName
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($Pptx)
    try {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry) {
            throw "Missing PPTX entry '$EntryName'."
        }
        $stream = $entry.Open()
        try {
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                return [Convert]::ToHexString(
                    $sha.ComputeHash($stream)
                )
            }
            finally {
                $sha.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ZipEntryNames {
    param(
        [Parameter(Mandatory)][string]$Pptx,
        [Parameter(Mandatory)][string]$Prefix
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($Pptx)
    try {
        return @(
            $archive.Entries |
                Where-Object {
                    $_.FullName.StartsWith(
                        $Prefix,
                        [StringComparison]::Ordinal
                    ) -and
                    -not $_.FullName.EndsWith("/")
                } |
                ForEach-Object { $_.FullName } |
                Sort-Object
        )
    }
    finally {
        $archive.Dispose()
    }
}

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory)][IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory)][string]$EntryName
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "Missing PPTX entry '$EntryName'."
    }
    $stream = $entry.Open()
    $reader = [IO.StreamReader]::new($stream)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Set-ZipEntryText {
    param(
        [Parameter(Mandatory)][IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory)][string]$EntryName,
        [Parameter(Mandatory)][string]$Content
    )

    $existing = $Archive.GetEntry($EntryName)
    if ($null -ne $existing) {
        $existing.Delete()
    }
    $entry = $Archive.CreateEntry($EntryName)
    $stream = $entry.Open()
    $writer = [IO.StreamWriter]::new(
        $stream,
        [Text.UTF8Encoding]::new($false)
    )
    try {
        $writer.Write($Content)
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function Add-TemplatePicture {
    param(
        [Parameter(Mandatory)][string]$Pptx,
        [Parameter(Mandatory)][int]$SlideNumber,
        [Parameter(Mandatory)][string]$RelationshipId,
        [Parameter(Mandatory)][string]$MediaName,
        [Parameter(Mandatory)][int]$PictureId
    )

    $archive = [IO.Compression.ZipFile]::Open(
        $Pptx,
        [IO.Compression.ZipArchiveMode]::Update
    )
    try {
        $contentTypes = Read-ZipEntryText `
            -Archive $archive `
            -EntryName "[Content_Types].xml"
        if ($contentTypes -notmatch 'Extension="png"') {
            $contentTypes = $contentTypes.Replace(
                "</Types>",
                '<Default Extension="png" ContentType="image/png"/></Types>'
            )
            Set-ZipEntryText `
                -Archive $archive `
                -EntryName "[Content_Types].xml" `
                -Content $contentTypes
        }

        $relationshipPart = "ppt/slides/_rels/slide$SlideNumber.xml.rels"
        $relationships = Read-ZipEntryText `
            -Archive $archive `
            -EntryName $relationshipPart
        $imageRelationship = @"
<Relationship Id="$RelationshipId" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/$MediaName"/>
"@
        $relationships = $relationships.Replace(
            "</Relationships>",
            "$imageRelationship</Relationships>"
        )
        Set-ZipEntryText `
            -Archive $archive `
            -EntryName $relationshipPart `
            -Content $relationships

        $slidePart = "ppt/slides/slide$SlideNumber.xml"
        $slide = Read-ZipEntryText `
            -Archive $archive `
            -EntryName $slidePart
        $picture = @"
<p:pic>
  <p:nvPicPr>
    <p:cNvPr id="$PictureId" name="Template Picture $SlideNumber"/>
    <p:cNvPicPr/>
    <p:nvPr/>
  </p:nvPicPr>
  <p:blipFill>
    <a:blip r:embed="$RelationshipId"/>
    <a:stretch><a:fillRect/></a:stretch>
  </p:blipFill>
  <p:spPr>
    <a:xfrm>
      <a:off x="100000" y="100000"/>
      <a:ext cx="100000" cy="100000"/>
    </a:xfrm>
    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
  </p:spPr>
</p:pic>
"@
        $slide = $slide.Replace(
            "</p:spTree>",
            "$picture</p:spTree>"
        )
        Set-ZipEntryText `
            -Archive $archive `
            -EntryName $slidePart `
            -Content $slide

        $mediaEntry = $archive.CreateEntry("ppt/media/$MediaName")
        $mediaStream = $mediaEntry.Open()
        try {
            $pngBytes = [Convert]::FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n8sAAAAASUVORK5CYII="
            )
            $mediaStream.Write($pngBytes, 0, $pngBytes.Length)
        }
        finally {
            $mediaStream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Replace-ZipEntryText {
    param(
        [Parameter(Mandatory)][string]$Pptx,
        [Parameter(Mandatory)][string]$EntryName,
        [Parameter(Mandatory)][string]$OldValue,
        [Parameter(Mandatory)][string]$NewValue
    )

    $archive = [IO.Compression.ZipFile]::Open(
        $Pptx,
        [IO.Compression.ZipArchiveMode]::Update
    )
    try {
        $content = Read-ZipEntryText `
            -Archive $archive `
            -EntryName $EntryName
        if (-not $content.Contains($OldValue)) {
            throw "PPTX entry '$EntryName' does not contain '$OldValue'."
        }
        Set-ZipEntryText `
            -Archive $archive `
            -EntryName $EntryName `
            -Content $content.Replace($OldValue, $NewValue)
    }
    finally {
        $archive.Dispose()
    }
}

$work = Join-Path $env:TEMP "mermaid2pptx-template-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
$template = Join-Path $work "template.pptx"
$scratch = Join-Path $work "with-diagram-01.pptx"
$final = Join-Path $work "final.pptx"
$pictureTamper = Join-Path $work "picture-tamper.pptx"
$targetTamper = Join-Path $work "target-tamper.pptx"
$targetModeTamper = Join-Path $work "target-mode-tamper.pptx"

try {
    & dotnet run --no-build --project $project -- `
        --html (Join-Path $repo "samples/all-diagrams.html") `
        --out $template `
        --slide-selector ".slide" `
        --svg-selector "svg"
    Assert-Equal 0 $LASTEXITCODE "template fixture conversion"
    Add-TemplatePicture `
        -Pptx $template `
        -SlideNumber 1 `
        -RelationshipId "rIdTemplateImage1" `
        -MediaName "template-target-01.png" `
        -PictureId 9001
    Add-TemplatePicture `
        -Pptx $template `
        -SlideNumber 2 `
        -RelationshipId "rIdTemplateImage2" `
        -MediaName "template-target-02.png" `
        -PictureId 9002
    Add-TemplatePicture `
        -Pptx $template `
        -SlideNumber 7 `
        -RelationshipId "rIdTemplateImage7" `
        -MediaName "template-nontarget-07.png" `
        -PictureId 9007
    $templateHash = (Get-FileHash $template -Algorithm SHA256).Hash

    & $invoke `
        -RepoRoot $repo `
        -Mermaid "flowchart LR; Request-->Route{Route}; Route-->Service" `
        -InsertInto $template `
        -Map "1=1" `
        -Out $scratch
    $stateDiagram = @"
stateDiagram-v2
    [*] --> Build
    Build --> Deploy
    Deploy --> [*]
"@
    & $invoke `
        -RepoRoot $repo `
        -Mermaid $stateDiagram `
        -InsertInto $scratch `
        -Map "2=1" `
        -Out $final

    Assert-Equal (
        $templateHash
    ) (Get-FileHash $template -Algorithm SHA256).Hash "template source hash"
    Assert-True (Test-Path -LiteralPath $final) "template final output"

    $auditResults = @(
        & $audit `
            -Pptx $final `
            -BaselinePptx $template `
            -DiagramSlides 1,2
    )
    Assert-Equal 2 $auditResults.Count "two mapped slide audits"

    foreach ($slideNumber in 3..7) {
        $entry = "ppt/slides/slide$slideNumber.xml"
        Assert-Equal (
            Get-ZipEntryHash -Pptx $template -EntryName $entry
        ) (
            Get-ZipEntryHash -Pptx $final -EntryName $entry
        ) "non-target slide $slideNumber preserved"
    }
    Assert-Equal (
        Get-ZipEntryHash `
            -Pptx $template `
            -EntryName "ppt/slides/_rels/slide7.xml.rels"
    ) (
        Get-ZipEntryHash `
            -Pptx $final `
            -EntryName "ppt/slides/_rels/slide7.xml.rels"
    ) "non-target image relationship preserved"

    foreach ($prefix in @(
        "ppt/media/",
        "ppt/slideMasters/",
        "ppt/slideLayouts/",
        "ppt/theme/"
    )) {
        $templateEntries = @(Get-ZipEntryNames -Pptx $template -Prefix $prefix)
        $finalEntries = @(Get-ZipEntryNames -Pptx $final -Prefix $prefix)
        Assert-Equal (
            $templateEntries -join "|"
        ) (
            $finalEntries -join "|"
        ) "$prefix entry set"
        foreach ($entry in $templateEntries) {
            Assert-Equal (
                Get-ZipEntryHash -Pptx $template -EntryName $entry
            ) (
                Get-ZipEntryHash -Pptx $final -EntryName $entry
            ) "$entry preserved"
        }
    }

    Copy-Item -LiteralPath $final -Destination $pictureTamper
    Replace-ZipEntryText `
        -Pptx $pictureTamper `
        -EntryName "ppt/slides/slide1.xml" `
        -OldValue "Template Picture 1" `
        -NewValue "Tampered Picture 1"
    Assert-Throws {
        & $audit `
            -Pptx $pictureTamper `
            -BaselinePptx $template `
            -DiagramSlides 1
    } "changed existing p:pic content"

    Copy-Item -LiteralPath $final -Destination $targetTamper
    Replace-ZipEntryText `
        -Pptx $targetTamper `
        -EntryName "ppt/slides/_rels/slide1.xml.rels" `
        -OldValue '../media/template-target-01.png' `
        -NewValue '../media/template-target-02.png'
    Assert-Throws {
        & $audit `
            -Pptx $targetTamper `
            -BaselinePptx $template `
            -DiagramSlides 1
    } "changed existing image relationships"

    Copy-Item -LiteralPath $final -Destination $targetModeTamper
    Replace-ZipEntryText `
        -Pptx $targetModeTamper `
        -EntryName "ppt/slides/_rels/slide1.xml.rels" `
        -OldValue 'Target="../media/template-target-01.png"' `
        -NewValue 'Target="../media/template-target-01.png" TargetMode="External"'
    Assert-Throws {
        & $audit `
            -Pptx $targetModeTamper `
            -BaselinePptx $template `
            -DiagramSlides 1
    } "changed existing image relationships"
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP)
    Assert-True (
        $resolvedWork.StartsWith(
            $resolvedTemp,
            [StringComparison]::OrdinalIgnoreCase
        )
    ) "template temporary work path"
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force
}
