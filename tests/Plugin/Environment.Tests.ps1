. "$PSScriptRoot/TestSupport.ps1"

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$script = Join-Path $repo "plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Initialize-Mermaid2Pptx.ps1"

Assert-True (Test-Path -LiteralPath $script) "initializer exists"

$info = & $script -RepoRoot $repo -SkipBrowserInstall
Assert-Equal ([IO.Path]::GetFullPath($repo)) $info.RepoRoot "explicit repo root"
Assert-True (Test-Path -LiteralPath $info.CliPath -PathType Leaf) "core CLI path"
Assert-True (Test-Path -LiteralPath $info.QaCliPath -PathType Leaf) "QA CLI path"

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

$fakeRoot = Join-Path $env:TEMP "mermaid2pptx-environment-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory `
    -Path (Join-Path $fakeRoot "src/Mermaid2Pptx/obj") `
    -Force | Out-Null
New-Item -ItemType Directory `
    -Path (Join-Path $fakeRoot "src/Mermaid2Pptx/bin/Debug/net8.0") `
    -Force | Out-Null
New-Item -ItemType Directory `
    -Path (Join-Path $fakeRoot "src/Mermaid2Pptx.Qa/bin/Debug/net8.0") `
    -Force | Out-Null
$fakeDotnet = Join-Path $fakeRoot "fake-dotnet.cmd"
$fakeLog = Join-Path $fakeRoot "dotnet.log"

try {
    Set-Content `
        -LiteralPath (Join-Path $fakeRoot "Mermaid2Pptx.sln") `
        -Value "fake solution"
    Set-Content `
        -LiteralPath (Join-Path $fakeRoot "src/Mermaid2Pptx/Mermaid2Pptx.csproj") `
        -Value "<Project />"
    Set-Content `
        -LiteralPath (Join-Path $fakeRoot "src/Mermaid2Pptx/obj/project.assets.json") `
        -Value "{}"
    Set-Content `
        -LiteralPath (Join-Path $fakeRoot "src/Mermaid2Pptx/bin/Debug/net8.0/playwright.ps1") `
        -Value "exit 0"
    $executableSuffix = if ($env:OS -eq "Windows_NT") {
        ".exe"
    }
    else {
        ""
    }
    Set-Content `
        -LiteralPath (Join-Path $fakeRoot "src/Mermaid2Pptx/bin/Debug/net8.0/Mermaid2Pptx$executableSuffix") `
        -Value "fake core CLI"
    Set-Content `
        -LiteralPath (Join-Path $fakeRoot "src/Mermaid2Pptx.Qa/bin/Debug/net8.0/Mermaid2Pptx.Qa$executableSuffix") `
        -Value "fake QA CLI"
    Set-Content -LiteralPath $fakeDotnet -Value @(
        "@echo off",
        "echo %*>>`"%FAKE_DOTNET_LOG%`"",
        "if `"%~1`"==`"--list-sdks`" echo 8.0.100 [fake]",
        "exit /b 0"
    )

    $previousFakeLog = $env:FAKE_DOTNET_LOG
    $env:FAKE_DOTNET_LOG = $fakeLog
    try {
        & $script `
            -RepoRoot $fakeRoot `
            -DotnetCommand $fakeDotnet `
            -SkipBrowserInstall | Out-Null
    }
    finally {
        if ($null -eq $previousFakeLog) {
            Remove-Item Env:FAKE_DOTNET_LOG -ErrorAction SilentlyContinue
        }
        else {
            $env:FAKE_DOTNET_LOG = $previousFakeLog
        }
    }

    $fakeCalls = @(Get-Content -LiteralPath $fakeLog)
    Assert-True (
        @($fakeCalls | Where-Object { $_ -match "^restore " }).Count -eq 1
    ) "initializer restores even when only core assets exist"
    Assert-True (
        @($fakeCalls | Where-Object { $_ -match "^build " }).Count -eq 1
    ) "initializer builds after restore"
}
finally {
    $resolvedFakeRoot = [IO.Path]::GetFullPath($fakeRoot)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP)
    Assert-True (
        $resolvedFakeRoot.StartsWith(
            $resolvedTemp,
            [StringComparison]::OrdinalIgnoreCase
        )
    ) "environment temporary work path"
    Remove-Item -LiteralPath $resolvedFakeRoot -Recurse -Force
}
