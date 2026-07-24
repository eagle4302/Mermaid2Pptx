. "$PSScriptRoot/TestSupport.ps1"

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$script = Join-Path $repo "plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Initialize-Mermaid2Pptx.ps1"

Assert-True (Test-Path -LiteralPath $script) "initializer exists"

$info = & $script -RepoRoot $repo -SkipBrowserInstall
Assert-Equal ([IO.Path]::GetFullPath($repo)) $info.RepoRoot "explicit repo root"

$previousRepo = $env:MERMAID2PPTX_REPO
Push-Location $env:TEMP
try {
    Remove-Item Env:MERMAID2PPTX_REPO -ErrorAction SilentlyContinue
    $fromScriptPath = & $script -SkipBrowserInstall
    Assert-Equal $info.RepoRoot $fromScriptPath.RepoRoot "script-relative discovery"

    $env:MERMAID2PPTX_REPO = $repo
    $fromEnvironment = & $script -SkipBrowserInstall
    Assert-Equal $info.RepoRoot $fromEnvironment.RepoRoot "environment discovery"
}
finally {
    Pop-Location
    if ($null -eq $previousRepo) {
        Remove-Item Env:MERMAID2PPTX_REPO -ErrorAction SilentlyContinue
    }
    else {
        $env:MERMAID2PPTX_REPO = $previousRepo
    }
}

Assert-Throws {
    & $script -RepoRoot (Join-Path $env:TEMP "missing-mermaid2pptx") -SkipBrowserInstall
} "Could not locate"

Assert-Throws {
    & $script -RepoRoot $repo -DotnetCommand "missing-dotnet-command" -SkipBrowserInstall
} "dotnet"
