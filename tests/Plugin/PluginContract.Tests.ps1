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
Assert-Equal "0.1.0" $manifest.version "manifest version"
Assert-Equal "./skills/" $manifest.skills "skill path"
Assert-True ($manifest.PSObject.Properties.Name -notcontains "mcpServers") "manifest omits MCP"
Assert-True ($manifest.PSObject.Properties.Name -notcontains "apps") "manifest omits apps"

Assert-Equal "mermaid2pptx-team" $marketplace.name "marketplace name"
$entry = @($marketplace.plugins | Where-Object name -eq $manifest.name)
Assert-Equal 1 $entry.Count "one marketplace entry"
Assert-Equal "./plugins/mermaid2pptx-presentations" $entry[0].source.path "local source"
Assert-Equal "AVAILABLE" $entry[0].policy.installation "install policy"
Assert-Equal "ON_INSTALL" $entry[0].policy.authentication "auth policy"
