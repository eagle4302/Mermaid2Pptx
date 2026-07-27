. "$PSScriptRoot/TestSupport.ps1"

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$plugin = Join-Path $repo "plugins/mermaid2pptx-presentations"
$manifestPath = Join-Path $plugin ".codex-plugin/plugin.json"
$marketplacePath = Join-Path $repo ".agents/plugins/marketplace.json"

Assert-True (Test-Path -LiteralPath $manifestPath) "plugin manifest exists"
Assert-True (Test-Path -LiteralPath $marketplacePath) "marketplace exists"

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$marketplace = Get-Content -LiteralPath $marketplacePath -Raw | ConvertFrom-Json

Assert-Equal "mermaid2pptx-presentations" $manifest.name "manifest name"
Assert-True (
    $manifest.version -match '^0\.1\.0\+codex\.\d{14}$'
) "manifest version has a local-development cachebuster"
Assert-Equal "./skills/" $manifest.skills "skill path"
Assert-True ($manifest.PSObject.Properties.Name -notcontains "mcpServers") "manifest omits MCP"
Assert-True ($manifest.PSObject.Properties.Name -notcontains "apps") "manifest omits apps"

Assert-Equal "mermaid2pptx-team" $marketplace.name "marketplace name"
$entry = @($marketplace.plugins | Where-Object name -eq $manifest.name)
Assert-Equal 1 $entry.Count "one marketplace entry"
Assert-Equal "./plugins/mermaid2pptx-presentations" $entry[0].source.path "local source"
Assert-Equal "AVAILABLE" $entry[0].policy.installation "install policy"
Assert-Equal "ON_INSTALL" $entry[0].policy.authentication "auth policy"

$skillRoot = Join-Path $plugin "skills/mermaid2pptx-presentations"
$requiredSkillFiles = @(
    "SKILL.md",
    "agents/openai.yaml",
    "references/cli-and-qa.md",
    "references/native-shape-invariants.md",
    "references/presentations-integration.md",
    "scripts/Initialize-Mermaid2Pptx.ps1",
    "scripts/Invoke-Mermaid2Pptx.ps1",
    "scripts/Invoke-Mermaid2PptxQa.ps1",
    "scripts/Publish-Mermaid2PptxCandidate.ps1",
    "scripts/Test-Mermaid2PptxDeck.ps1"
)
foreach ($requiredSkillFile in $requiredSkillFiles) {
    Assert-True (
        Test-Path -LiteralPath (Join-Path $skillRoot $requiredSkillFile) `
            -PathType Leaf
    ) "skill file exists: $requiredSkillFile"
}

$publicScripts = @(
    Get-ChildItem -LiteralPath (Join-Path $skillRoot "scripts") `
        -Filter "*.ps1" `
        -File
)
foreach ($script in $publicScripts) {
    Assert-Equal "#requires -Version 5.1" (
        Get-Content -LiteralPath $script.FullName -TotalCount 1
    ) "PowerShell version preflight: $($script.Name)"
}

$invocationScriptText = Get-Content `
    -LiteralPath (Join-Path $skillRoot "scripts/Invoke-Mermaid2Pptx.ps1") `
    -Raw
$qaScriptText = Get-Content `
    -LiteralPath (Join-Path $skillRoot "scripts/Invoke-Mermaid2PptxQa.ps1") `
    -Raw
Assert-True (
    $invocationScriptText -notmatch '\bdotnet\s+run\b'
) "invocation wrapper avoids dotnet run"
Assert-True (
    $qaScriptText -notmatch '\bdotnet\s+run\b'
) "QA wrapper avoids dotnet run"

$skillText = Get-Content -LiteralPath (Join-Path $skillRoot "SKILL.md") -Raw
Assert-True (
    $skillText -notmatch [regex]::Escape("E:\project\Mermaid2PPTX")
) "skill contains no machine-specific repository path"
Assert-True (
    $skillText -match "PowerShell 5\.1"
) "skill documents PowerShell 5.1 compatibility"
Assert-True (
    $skillText -match [regex]::Escape('`mermaid2pptx` CLI')
) "skill documents the installable CLI"

$openAiMetadata = Get-Content `
    -LiteralPath (Join-Path $skillRoot "agents/openai.yaml") `
    -Raw
Assert-True (
    $openAiMetadata -match [regex]::Escape(
        '$mermaid2pptx-presentations'
    )
) "default prompt names the skill"

$forbiddenArtifacts = @(
    Get-ChildItem -LiteralPath $plugin -Recurse -Force | Where-Object {
        $_.FullName -match '[\\/](bin|obj)([\\/]|$)' -or
        $_.Extension -ieq ".pptx" -or
        $_.FullName -match '[\\/]ppt[\\/]media([\\/]|$)'
    }
)
Assert-Equal 0 $forbiddenArtifacts.Count "plugin contains no generated artifacts"
