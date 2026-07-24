[CmdletBinding(DefaultParameterSetName = "MermaidFile")]
param(
    [Parameter(Mandatory, ParameterSetName = "Mermaid")]
    [string]$Mermaid,

    [Parameter(Mandatory, ParameterSetName = "MermaidFile")]
    [string]$MermaidFile,

    [Parameter(Mandatory, ParameterSetName = "MermaidStdin")]
    [switch]$MermaidStdin,

    [Parameter(ValueFromPipeline, ParameterSetName = "MermaidStdin")]
    [AllowEmptyString()]
    [string]$MermaidInput,

    [Parameter(Mandatory, ParameterSetName = "Html")]
    [string]$Html,

    [Parameter(Mandatory, ParameterSetName = "SourcePptx")]
    [string]$SourcePptx,

    [Parameter(Mandatory)]
    [string]$Out,

    [string]$InsertInto,
    [string]$Map,
    [string]$RepoRoot = $env:MERMAID2PPTX_REPO,
    [string]$SlideSelector = ".slide",
    [string]$SvgSelector = "svg",
    [double]$Width = 13.333,
    [double]$Height = 7.5,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-ExistingFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Label
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "$Label does not exist: $fullPath"
    }
    return $fullPath
}

function Assert-DifferentPath {
    param(
        [Parameter(Mandatory)][string]$OutputPath,
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$SourceLabel
    )

    if ($OutputPath.Equals(
        $SourcePath,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Output path must differ from $SourceLabel path: $OutputPath"
    }
}

function Get-TargetSlideNumbers {
    param([Parameter(Mandatory)][string]$Mapping)

    $targets = @()
    foreach ($entry in $Mapping.Split(
        ",",
        [StringSplitOptions]::RemoveEmptyEntries
    )) {
        if ($entry -notmatch "^\s*(\d+)\s*=\s*(\d+)\s*$") {
            throw "Invalid slide map entry '$entry'. Use target=source pairs such as 5=1."
        }

        $target = [int]$Matches[1]
        $source = [int]$Matches[2]
        if ($target -lt 1 -or $source -lt 1) {
            throw "Slide map values must be 1-based positive integers: '$entry'."
        }
        $targets += $target
    }

    if ($targets.Count -eq 0) {
        throw "Insert mode requires at least one target=source slide map."
    }

    return @($targets | Select-Object -Unique)
}

function Get-PresentationSlideCount {
    param([Parameter(Mandatory)][string]$Path)

    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry("ppt/presentation.xml")
        if ($null -eq $entry) {
            throw "PPTX package is missing ppt/presentation.xml: $Path"
        }
        $stream = $entry.Open()
        $reader = [IO.StreamReader]::new($stream)
        try {
            [xml]$presentation = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
            $stream.Dispose()
        }
        return $presentation.SelectNodes(
            "//*[local-name()='sldId']"
        ).Count
    }
    finally {
        $archive.Dispose()
    }
}

$outputPath = [IO.Path]::GetFullPath($Out)
if (-not $outputPath.EndsWith(".pptx", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Output path must end with .pptx: $outputPath"
}
if ((Test-Path -LiteralPath $outputPath) -and -not $Force) {
    throw "Output file already exists. Pass -Force to replace it: $outputPath"
}

$insertPath = $null
$targetSlides = @()
if (-not [string]::IsNullOrWhiteSpace($InsertInto)) {
    $insertPath = Resolve-ExistingFile `
        -Path $InsertInto `
        -Label "Insert source PPTX"
    if ([string]::IsNullOrWhiteSpace($Map)) {
        throw "Insert mode requires -Map with target=source slide pairs."
    }
    $targetSlides = Get-TargetSlideNumbers -Mapping $Map
    Assert-DifferentPath `
        -OutputPath $outputPath `
        -SourcePath $insertPath `
        -SourceLabel "insert source"
    $targetSlideCount = Get-PresentationSlideCount -Path $insertPath
    foreach ($targetSlide in $targetSlides) {
        if ($targetSlide -gt $targetSlideCount) {
            throw "Target slide $targetSlide is outside the target deck slide range 1-$targetSlideCount."
        }
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($Map)) {
    throw "-Map can only be used with -InsertInto."
}

if ($PSCmdlet.ParameterSetName -eq "SourcePptx" -and
    [string]::IsNullOrWhiteSpace($InsertInto)) {
    throw "-SourcePptx requires -InsertInto."
}
if ($PSCmdlet.ParameterSetName -eq "Mermaid" -and
    [string]::IsNullOrWhiteSpace($Mermaid)) {
    throw "Mermaid source cannot be empty."
}
if ($PSCmdlet.ParameterSetName -eq "MermaidStdin" -and
    [string]::IsNullOrWhiteSpace($MermaidInput)) {
    throw "Mermaid stdin source cannot be empty. Pipe one raw string, for example Get-Content diagram.mmd -Raw."
}

$sourcePath = $null
switch ($PSCmdlet.ParameterSetName) {
    "MermaidFile" {
        $sourcePath = Resolve-ExistingFile `
            -Path $MermaidFile `
            -Label "Mermaid file"
    }
    "Html" {
        $sourcePath = Resolve-ExistingFile `
            -Path $Html `
            -Label "HTML file"
    }
    "SourcePptx" {
        $sourcePath = Resolve-ExistingFile `
            -Path $SourcePptx `
            -Label "Source PPTX"
    }
}
if ($null -ne $sourcePath) {
    Assert-DifferentPath `
        -OutputPath $outputPath `
        -SourcePath $sourcePath `
        -SourceLabel "conversion source"
}

$outputDirectory = Split-Path -Path $outputPath -Parent
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$candidateName = ".{0}.{1}.tmp.pptx" -f @(
    [IO.Path]::GetFileNameWithoutExtension($outputPath),
    [guid]::NewGuid().ToString("N")
)
$candidatePath = Join-Path $outputDirectory $candidateName
$stdinPath = $null
$environment = $null

try {
    $initializer = Join-Path $PSScriptRoot "Initialize-Mermaid2Pptx.ps1"
    $audit = Join-Path $PSScriptRoot "Test-Mermaid2PptxDeck.ps1"
    $publisher = Join-Path $PSScriptRoot "Publish-Mermaid2PptxCandidate.ps1"
    $environment = & $initializer -RepoRoot $RepoRoot

    $converterArguments = @()
    switch ($PSCmdlet.ParameterSetName) {
        "Mermaid" {
            $converterArguments += @("--mermaid", $Mermaid)
        }
        "MermaidFile" {
            $converterArguments += @("--mermaid-file", $sourcePath)
        }
        "MermaidStdin" {
            $stdinContent = $MermaidInput
            $stdinPath = Join-Path (
                [IO.Path]::GetTempPath()
            ) "mermaid2pptx-stdin-$([guid]::NewGuid().ToString('N')).mmd"
            [IO.File]::WriteAllText($stdinPath, $stdinContent)
            $converterArguments += @("--mermaid-file", $stdinPath)
        }
        "Html" {
            $converterArguments += @(
                "--html", $sourcePath,
                "--slide-selector", $SlideSelector,
                "--svg-selector", $SvgSelector
            )
        }
        "SourcePptx" {
            $converterArguments += @("--source-pptx", $sourcePath)
        }
    }

    if ($null -ne $insertPath) {
        $converterArguments += @(
            "--insert-into", $insertPath,
            "--map", $Map
        )
    }
    $converterArguments += @(
        "--out", $candidatePath,
        "--width", $Width.ToString(
            [Globalization.CultureInfo]::InvariantCulture
        ),
        "--height", $Height.ToString(
            [Globalization.CultureInfo]::InvariantCulture
        )
    )

    $commandOutput = @(
        & dotnet run `
            --no-build `
            --project $environment.ProjectPath `
            -- @converterArguments 2>&1
    )
    $conversionExitCode = $LASTEXITCODE
    $commandOutput | ForEach-Object { Write-Host $_ }
    if ($conversionExitCode -ne 0) {
        throw "Mermaid2Pptx conversion failed. $($commandOutput -join [Environment]::NewLine)"
    }
    if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
        throw "Mermaid2Pptx reported success without writing the candidate PPTX."
    }

    if ($null -eq $insertPath) {
        $auditResults = @(
            & $audit -Pptx $candidatePath -Standalone
        )
    }
    else {
        $auditResults = @(
            & $audit `
                -Pptx $candidatePath `
                -BaselinePptx $insertPath `
                -DiagramSlides $targetSlides
        )
    }
    Write-Host "Validated Mermaid slides: $($auditResults.Count)"

    & $publisher `
        -Candidate $candidatePath `
        -Destination $outputPath `
        -Force:$Force | Out-Null
    $outputPath
}
catch {
    if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
        $diagnosticRoot = if ($null -ne $environment) {
            Join-Path $environment.RepoRoot "out/plugin-diagnostics"
        }
        else {
            Join-Path $outputDirectory "plugin-diagnostics"
        }
        New-Item -ItemType Directory -Path $diagnosticRoot -Force | Out-Null
        $diagnosticName = "{0}-{1}-{2}" -f @(
            (Get-Date -Format "yyyyMMdd-HHmmss"),
            [guid]::NewGuid().ToString("N"),
            [IO.Path]::GetFileName($outputPath)
        )
        $diagnosticPath = Join-Path $diagnosticRoot $diagnosticName
        Move-Item -LiteralPath $candidatePath -Destination $diagnosticPath
        throw [InvalidOperationException]::new(
            "$($_.Exception.Message) Candidate preserved at '$diagnosticPath'.",
            $_.Exception
        )
    }
    throw
}
finally {
    if ($null -ne $stdinPath -and
        (Test-Path -LiteralPath $stdinPath -PathType Leaf)) {
        Remove-Item -LiteralPath $stdinPath -Force
    }
}
