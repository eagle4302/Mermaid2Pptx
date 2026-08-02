#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Pptx,
    [string]$BaselinePptx,
    [int[]]$DiagramSlides,
    [switch]$Standalone
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory)][IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory)][string]$EntryName
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "PPTX package is missing required part '$EntryName'."
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

function Resolve-PackageTarget {
    param(
        [Parameter(Mandatory)][string]$SourcePart,
        [Parameter(Mandatory)][string]$Target
    )

    $baseUri = [Uri]::new("https://package.local/$SourcePart")
    $targetUri = [Uri]::new($baseUri, $Target)
    return $targetUri.AbsolutePath.TrimStart("/")
}

function Get-SlideRelationshipPart {
    param([Parameter(Mandatory)][string]$SlidePart)

    $directory = [IO.Path]::GetDirectoryName($SlidePart).Replace("\", "/")
    $fileName = [IO.Path]::GetFileName($SlidePart)
    return "$directory/_rels/$fileName.rels"
}

function Get-SlideTextMetrics {
    param([Parameter(Mandatory)][xml]$SlideXml)

    $shapeEmbeddedTextCount = 0
    $textBoxCount = 0
    $presetNodeShapeCount = 0
    $presetNodeGeometries = @(
        "rect",
        "roundRect",
        "ellipse",
        "diamond",
        "hexagon",
        "octagon",
        "pentagon",
        "triangle",
        "trapezoid",
        "can"
    )

    foreach ($shape in @($SlideXml.SelectNodes("//*[local-name()='sp']"))) {
        $cNvSpPr = $shape.SelectSingleNode(".//*[local-name()='cNvSpPr']")
        $isTextBox = $false
        if ($null -ne $cNvSpPr) {
            $txBoxValue = $cNvSpPr.GetAttribute("txBox")
            $isTextBox = $txBoxValue -eq "1"
        }

        $textNodes = @(
            $shape.SelectNodes(".//*[local-name()='txBody']//*[local-name()='t']") |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_.InnerText) }
        )
        $hasText = $textNodes.Count -gt 0

        if ($isTextBox) {
            if ($hasText) {
                $textBoxCount++
            }
            continue
        }

        if ($hasText) {
            $shapeEmbeddedTextCount++
        }

        $preset = $shape.SelectSingleNode(".//*[local-name()='prstGeom']")
        if ($null -ne $preset) {
            $geometry = $preset.GetAttribute("prst")
            if ($presetNodeGeometries -contains $geometry) {
                $presetNodeShapeCount++
            }
        }
    }

    return [PSCustomObject]@{
        ShapeEmbeddedTextCount = $shapeEmbeddedTextCount
        TextBoxCount = $textBoxCount
        PresetNodeShapeCount = $presetNodeShapeCount
    }
}

function Get-PackageFontFacts {
    param([Parameter(Mandatory)][IO.Compression.ZipArchive]$Archive)

    $windowsOnlyFonts = @(
        "Microsoft JhengHei",
        "Microsoft YaHei",
        "DengXian"
    )
    $parts = @(
        $Archive.Entries |
            Where-Object {
                $_.FullName -like "ppt/slideMasters/*.xml" -or
                $_.FullName -like "ppt/slideMasters/theme/*.xml" -or
                $_.FullName -like "ppt/theme/*.xml"
            } |
            ForEach-Object { $_.FullName }
    )

    $matchedWindowsOnlyFonts = @()
    $hasArialTypeface = $false
    foreach ($part in $parts) {
        $xmlText = Read-ZipEntryText -Archive $Archive -EntryName $part
        foreach ($font in $windowsOnlyFonts) {
            if ($xmlText.Contains($font)) {
                $matchedWindowsOnlyFonts += $font
            }
        }
        if ($xmlText -match 'typeface\s*=\s*"Arial"') {
            $hasArialTypeface = $true
        }
    }

    return [PSCustomObject]@{
        Parts = $parts
        WindowsOnlyFonts = @($matchedWindowsOnlyFonts | Sort-Object -Unique)
        HasArialTypeface = $hasArialTypeface
    }
}

function Get-PptxFacts {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "PPTX file does not exist: $fullPath"
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($fullPath)
    try {
        $requiredParts = @(
            "[Content_Types].xml",
            "ppt/presentation.xml",
            "ppt/_rels/presentation.xml.rels"
        )
        foreach ($requiredPart in $requiredParts) {
            if ($null -eq $archive.GetEntry($requiredPart)) {
                throw "PPTX package is missing required part '$requiredPart'."
            }
        }

        [xml]$presentation = Read-ZipEntryText `
            -Archive $archive `
            -EntryName "ppt/presentation.xml"
        [xml]$presentationRelationships = Read-ZipEntryText `
            -Archive $archive `
            -EntryName "ppt/_rels/presentation.xml.rels"

        $relationshipTargets = @{}
        foreach ($relationship in $presentationRelationships.SelectNodes(
            "//*[local-name()='Relationship']"
        )) {
            $relationshipTargets[$relationship.Id] = $relationship.Target
        }

        $slides = @()
        $slideNumber = 0
        foreach ($slideId in $presentation.SelectNodes(
            "//*[local-name()='sldId']"
        )) {
            $slideNumber++
            $relationshipId = $slideId.GetAttribute(
                "id",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
            )
            if (-not $relationshipTargets.ContainsKey($relationshipId)) {
                throw "Slide $slideNumber references missing relationship '$relationshipId'."
            }

            $slidePart = Resolve-PackageTarget `
                -SourcePart "ppt/presentation.xml" `
                -Target $relationshipTargets[$relationshipId]
            [xml]$slideXml = Read-ZipEntryText `
                -Archive $archive `
                -EntryName $slidePart

            $slideRelationshipPart = Get-SlideRelationshipPart `
                -SlidePart $slidePart
            $imageRelationshipCount = 0
            $imageRelationshipSignatures = @()
            $slideRelationshipEntry = $archive.GetEntry($slideRelationshipPart)
            if ($null -ne $slideRelationshipEntry) {
                [xml]$slideRelationships = Read-ZipEntryText `
                    -Archive $archive `
                    -EntryName $slideRelationshipPart
                $imageRelationships = @(
                    $slideRelationships.SelectNodes(
                        "//*[local-name()='Relationship']"
                    ) | Where-Object {
                        $_.Type -match "/image$"
                    }
                )
                $imageRelationshipCount = $imageRelationships.Count
                $imageRelationshipSignatures = @(
                    $imageRelationships | ForEach-Object {
                        "$($_.Id)|$($_.Type)|$($_.Target)|$($_.GetAttribute('TargetMode'))"
                    } | Sort-Object
                )
            }

            $pictures = @(
                $slideXml.SelectNodes(
                    "//*[local-name()='pic']"
                )
            )
            $textMetrics = Get-SlideTextMetrics -SlideXml $slideXml
            $slides += [PSCustomObject]@{
                Number = $slideNumber
                Part = $slidePart
                ShapeCount = $slideXml.SelectNodes(
                    "//*[local-name()='sp' or local-name()='cxnSp']"
                ).Count
                PictureCount = $pictures.Count
                PictureSignatures = @(
                    $pictures | ForEach-Object {
                        $_.OuterXml
                    }
                )
                ImageRelationshipCount = $imageRelationshipCount
                ImageRelationshipSignatures = $imageRelationshipSignatures
                TextValues = @(
                    $slideXml.SelectNodes(
                        "//*[local-name()='t']"
                    ) | ForEach-Object {
                        $_.InnerText
                    }
                )
                ShapeEmbeddedTextCount = $textMetrics.ShapeEmbeddedTextCount
                TextBoxCount = $textMetrics.TextBoxCount
                PresetNodeShapeCount = $textMetrics.PresetNodeShapeCount
            }
        }

        if ($slides.Count -eq 0) {
            throw "PPTX package contains no slides."
        }

        $mediaEntries = @(
            $archive.Entries | Where-Object {
                $_.FullName -like "ppt/media/*" -and
                -not $_.FullName.EndsWith("/")
            } | ForEach-Object {
                $_.FullName
            }
        )
        $fontFacts = Get-PackageFontFacts -Archive $archive

        return [PSCustomObject]@{
            Path = $fullPath
            Slides = $slides
            MediaEntries = $mediaEntries
            WindowsOnlyFonts = $fontFacts.WindowsOnlyFonts
            HasArialTypeface = $fontFacts.HasArialTypeface
        }
    }
    finally {
        $archive.Dispose()
    }
}

$candidate = Get-PptxFacts -Path $Pptx
if ($null -eq $DiagramSlides -or $DiagramSlides.Count -eq 0) {
    $targetSlides = 1..$candidate.Slides.Count
}
else {
    $targetSlides = @($DiagramSlides)
}

if ($Standalone) {
    if (-not [string]::IsNullOrWhiteSpace($BaselinePptx)) {
        throw "Standalone audit does not accept -BaselinePptx."
    }
    if ($candidate.MediaEntries.Count -gt 0) {
        throw "Standalone Mermaid deck contains media parts: $($candidate.MediaEntries -join ', ')"
    }
    if ($candidate.WindowsOnlyFonts.Count -gt 0) {
        throw "Standalone Mermaid deck uses Windows-only master/theme fonts: $($candidate.WindowsOnlyFonts -join ', ')"
    }
    if (-not $candidate.HasArialTypeface) {
        throw "Standalone Mermaid deck master/theme is missing portable Arial typefaces."
    }
    $baseline = $null
}
else {
    if ([string]::IsNullOrWhiteSpace($BaselinePptx)) {
        throw "Insertion audit requires -BaselinePptx."
    }
    $baseline = Get-PptxFacts -Path $BaselinePptx
}

$results = @()
foreach ($slideNumber in $targetSlides) {
    if ($slideNumber -lt 1 -or $slideNumber -gt $candidate.Slides.Count) {
        throw "Diagram slide number $slideNumber is outside candidate slide range 1-$($candidate.Slides.Count)."
    }

    $candidateSlide = $candidate.Slides[$slideNumber - 1]
    $renderErrors = @(
        $candidateSlide.TextValues | Where-Object {
            $_ -match "^\s*Syntax error in text\s*$"
        }
    )
    if ($renderErrors.Count -gt 0) {
        throw "Mermaid render error detected on diagram slide ${slideNumber}: Syntax error in text."
    }
    if ($candidateSlide.ShapeCount -le 0) {
        throw "Diagram slide $slideNumber contains no native shapes or connectors."
    }

    if ($Standalone) {
        if ($candidateSlide.PictureCount -ne 0) {
            throw "Standalone Mermaid slide $slideNumber contains p:pic elements."
        }
        $hasVisibleText = @(
            $candidateSlide.TextValues | Where-Object {
                -not [string]::IsNullOrWhiteSpace($_)
            }
        ).Count -gt 0
        # Flowchart-style 1:1 overlays: each preset node has a sibling txBox and
        # no shape-owned txBody. Multi-label containers (class/ER) usually have
        # more text boxes than node shapes and remain allowed.
        if (
            $hasVisibleText -and
            $candidateSlide.PresetNodeShapeCount -gt 0 -and
            $candidateSlide.ShapeEmbeddedTextCount -eq 0 -and
            $candidateSlide.TextBoxCount -gt 0 -and
            $candidateSlide.TextBoxCount -le $candidateSlide.PresetNodeShapeCount
        ) {
            throw "Standalone Mermaid slide $slideNumber keeps node labels in txBox overlays instead of shape txBody."
        }
        $baselineSlide = $null
    }
    else {
        if ($slideNumber -gt $baseline.Slides.Count) {
            throw "Diagram slide number $slideNumber is outside baseline slide range 1-$($baseline.Slides.Count)."
        }

        $baselineSlide = $baseline.Slides[$slideNumber - 1]
        if ($candidateSlide.ShapeCount -le $baselineSlide.ShapeCount) {
            throw "Diagram slide $slideNumber did not add native shapes."
        }
        if ($candidateSlide.PictureCount -ne $baselineSlide.PictureCount) {
            throw "Diagram slide $slideNumber changed p:pic count from $($baselineSlide.PictureCount) to $($candidateSlide.PictureCount)."
        }
        if (($candidateSlide.PictureSignatures -join "`n") -ne
            ($baselineSlide.PictureSignatures -join "`n")) {
            throw "Diagram slide $slideNumber changed existing p:pic content."
        }
        if ($candidateSlide.ImageRelationshipCount -ne
            $baselineSlide.ImageRelationshipCount) {
            throw "Diagram slide $slideNumber changed image relationship count from $($baselineSlide.ImageRelationshipCount) to $($candidateSlide.ImageRelationshipCount)."
        }
        if (($candidateSlide.ImageRelationshipSignatures -join "`n") -ne
            ($baselineSlide.ImageRelationshipSignatures -join "`n")) {
            throw "Diagram slide $slideNumber changed existing image relationships."
        }
    }

    $results += [PSCustomObject]@{
        SlideNumber = $slideNumber
        Part = $candidateSlide.Part
        ShapeCount = $candidateSlide.ShapeCount
        PictureCount = $candidateSlide.PictureCount
        ImageRelationshipCount = $candidateSlide.ImageRelationshipCount
        ShapeEmbeddedTextCount = $candidateSlide.ShapeEmbeddedTextCount
        TextBoxCount = $candidateSlide.TextBoxCount
        PresetNodeShapeCount = $candidateSlide.PresetNodeShapeCount
        BaselineShapeCount = if ($null -eq $baselineSlide) {
            $null
        }
        else {
            $baselineSlide.ShapeCount
        }
        BaselinePictureCount = if ($null -eq $baselineSlide) {
            $null
        }
        else {
            $baselineSlide.PictureCount
        }
        BaselineImageRelationshipCount = if ($null -eq $baselineSlide) {
            $null
        }
        else {
            $baselineSlide.ImageRelationshipCount
        }
    }
}

$results
