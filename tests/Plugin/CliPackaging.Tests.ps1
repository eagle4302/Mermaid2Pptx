. "$PSScriptRoot/TestSupport.ps1"

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$project = Join-Path $repo "src/Mermaid2Pptx/Mermaid2Pptx.csproj"
$audit = Join-Path $repo "plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Test-Mermaid2PptxDeck.ps1"
$work = Join-Path $env:TEMP "mermaid2pptx-tool-$([guid]::NewGuid().ToString('N'))"
$packages = Join-Path $work "packages"
$tools = Join-Path $work "tools"
New-Item -ItemType Directory -Path $packages -Force | Out-Null
New-Item -ItemType Directory -Path $tools -Force | Out-Null

try {
    $packOutput = @(
        & dotnet pack $project `
            --configuration Release `
            --output $packages 2>&1
    )
    Assert-Equal 0 $LASTEXITCODE "dotnet pack exit code: $($packOutput -join ' | ')"

    $package = @(
        Get-ChildItem -LiteralPath $packages `
            -Filter "Mermaid2Pptx.Tool.0.1.0.nupkg" `
            -File
    )
    Assert-Equal 1 $package.Count "one Mermaid2Pptx tool package"

    $installOutput = @(
        & dotnet tool install Mermaid2Pptx.Tool `
            --tool-path $tools `
            --add-source $packages `
            --version 0.1.0 `
            --ignore-failed-sources 2>&1
    )
    Assert-Equal 0 $LASTEXITCODE "dotnet tool install exit code: $($installOutput -join ' | ')"

    $cliName = if ($env:OS -eq "Windows_NT") {
        "mermaid2pptx.exe"
    }
    else {
        "mermaid2pptx"
    }
    $cli = Join-Path $tools $cliName
    Assert-True (Test-Path -LiteralPath $cli -PathType Leaf) "installed mermaid2pptx command"

    $helpOutput = @(& $cli --help 2>&1)
    Assert-Equal 0 $LASTEXITCODE "installed CLI help exit code"
    Assert-True (
        ($helpOutput -join [Environment]::NewLine) -match "mermaid2pptx setup"
    ) "installed CLI advertises setup"

    $setupOutput = @(& $cli setup 2>&1)
    Assert-Equal 0 $LASTEXITCODE "installed CLI setup exit code: $($setupOutput -join ' | ')"

    $deck = Join-Path $work "installed-tool.pptx"
    $convertOutput = @(
        & $cli `
            --mermaid "flowchart LR; Installed-->Native" `
            --out $deck 2>&1
    )
    Assert-Equal 0 $LASTEXITCODE "installed CLI conversion exit code: $($convertOutput -join ' | ')"
    Assert-True (Test-Path -LiteralPath $deck) "installed CLI output"

    & $audit -Pptx $deck -Standalone | Out-Null
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP)
    Assert-True (
        $resolvedWork.StartsWith(
            $resolvedTemp,
            [StringComparison]::OrdinalIgnoreCase
        )
    ) "CLI packaging temporary work path"
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force
}
