# Mermaid2Pptx Presentations Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and install a repository-local Codex plugin that coordinates complete PowerPoint production with `Presentations` and inserts editable native-shape Mermaid diagrams through the existing Mermaid2Pptx C# solution.

**Architecture:** Keep presentation narrative, layout, templates, and ordinary slides in the existing `Presentations` skill. Add a thin repository plugin containing one orchestration skill and focused PowerShell adapters for environment bootstrap, conversion/insertion, fast target-slide auditing, and optional full QA; do not change or duplicate the C# conversion pipeline.

**Tech Stack:** Codex plugin manifests, Agent Skills, PowerShell 7, .NET 8, Open XML package XML, Mermaid2Pptx CLI, Mermaid2Pptx.Qa, xUnit, Python plugin/skill validators.

## Global Constraints

- Plugin root: `plugins/mermaid2pptx-presentations`.
- Repository marketplace: `.agents/plugins/marketplace.json`.
- Plugin and skill name: `mermaid2pptx-presentations`.
- Marketplace name: `mermaid2pptx-team` because the installed personal marketplace already uses `personal`.
- Require .NET 8 and prepare Playwright Chromium automatically.
- Do not place user-specific absolute paths in plugin files.
- Do not add an MCP server, app manifest, published converter binary, browser files, generated PPTX, or routine QA output to source control.
- Preserve source templates and publish a final output only after validation passes.
- Mermaid diagrams must use native DrawingML shapes and text; converted diagrams must not introduce `p:pic`, image relationships, or `ppt/media/*`.
- Images already present in ordinary presentation content remain valid.
- Use fast package validation by default and full visual/XML QA only for explicit or high-risk runs.
- Do not modify `tools/SmartFactoryDeck`.

## File Map

- `.agents/plugins/marketplace.json`: repository-local marketplace entry.
- `plugins/mermaid2pptx-presentations/.codex-plugin/plugin.json`: plugin metadata and skill discovery.
- `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/SKILL.md`: agent orchestration contract.
- `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/agents/openai.yaml`: skill UI metadata.
- `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/references/*.md`: detailed CLI, invariant, and presentation integration guidance.
- `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Initialize-Mermaid2Pptx.ps1`: repository discovery and dependency preparation.
- `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Invoke-Mermaid2Pptx.ps1`: atomic conversion and insertion adapter.
- `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Test-Mermaid2PptxDeck.ps1`: standalone and baseline-comparison package audit.
- `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Invoke-Mermaid2PptxQa.ps1`: full QA adapter.
- `tests/Plugin/TestSupport.ps1`: assertion and isolated-process helpers without a Pester dependency.
- `tests/Plugin/PluginContract.Tests.ps1`: manifest, marketplace, and source-tree contract checks.
- `tests/Plugin/Environment.Tests.ps1`: repository discovery and dependency diagnostic checks.
- `tests/Plugin/DeckAudit.Tests.ps1`: PPTX audit positive and negative checks.
- `tests/Plugin/Invocation.Tests.ps1`: standalone, insertion, overwrite, mapping, and source-preservation checks.
- `tests/Plugin/Qa.Tests.ps1`: QA adapter validation and representative sample run.
- `tests/Plugin/Run-All.ps1`: deterministic plugin test entry point.

---

### Task 1: Scaffold The Repository Plugin And Lock Its Contract

**Files:**
- Create: `.agents/plugins/marketplace.json`
- Create: `plugins/mermaid2pptx-presentations/.codex-plugin/plugin.json`
- Create: `tests/Plugin/TestSupport.ps1`
- Create: `tests/Plugin/PluginContract.Tests.ps1`
- Create: `tests/Plugin/Run-All.ps1`

**Interfaces:**
- Consumes: official `plugin-creator/scripts/create_basic_plugin.py`.
- Produces: plugin root and marketplace contract used by every later task.

- [ ] **Step 1: Write the shared test support and failing manifest contract**

Create `tests/Plugin/TestSupport.ps1` with concrete assertions:

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "Assertion failed: $Message. Expected '$Expected', got '$Actual'."
    }
}

function Assert-Throws {
    param([scriptblock]$Action, [string]$Pattern)
    try { & $Action; throw "Expected action to throw: $Pattern" }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) {
            throw "Expected error matching '$Pattern', got '$($_.Exception.Message)'."
        }
    }
}
```

Create `tests/Plugin/PluginContract.Tests.ps1` to load both JSON files and assert:

```powershell
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
```

Create `tests/Plugin/Run-All.ps1`:

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$tests = Get-ChildItem -LiteralPath $PSScriptRoot -Filter "*.Tests.ps1" | Sort-Object Name
foreach ($test in $tests) {
    Write-Host "Running $($test.Name)"
    & $test.FullName
}
Write-Host "Plugin tests passed: $($tests.Count)"
```

- [ ] **Step 2: Run the contract test and verify RED**

Run:

```powershell
pwsh -NoProfile -File tests/Plugin/PluginContract.Tests.ps1
```

Expected: FAIL with `plugin manifest exists`.

- [ ] **Step 3: Scaffold with the official plugin creator**

Run from the repository root:

```powershell
$creator = "C:\Users\User\.codex\skills\.system\plugin-creator"
py -3 "$creator/scripts/create_basic_plugin.py" mermaid2pptx-presentations `
  --path plugins `
  --with-skills `
  --with-marketplace `
  --marketplace-path .agents/plugins/marketplace.json `
  --marketplace-name mermaid2pptx-team `
  --install-policy AVAILABLE `
  --auth-policy ON_INSTALL `
  --category Productivity
```

Expected: the plugin and marketplace paths are created without `.mcp.json` or `.app.json`.

- [ ] **Step 4: Replace scaffold metadata with the approved contract**

Set `.codex-plugin/plugin.json` to:

```json
{
  "name": "mermaid2pptx-presentations",
  "version": "0.1.0",
  "description": "Build PowerPoint decks and insert editable native-shape Mermaid diagrams with Mermaid2Pptx.",
  "author": {
    "name": "Mermaid2Pptx Contributors"
  },
  "keywords": ["powerpoint", "pptx", "mermaid", "drawingml", "presentations"],
  "skills": "./skills/",
  "interface": {
    "displayName": "Mermaid2Pptx Presentations",
    "shortDescription": "Editable Mermaid diagrams in complete PowerPoint decks.",
    "longDescription": "Coordinates complete presentation production and uses Mermaid2Pptx to insert editable native DrawingML diagrams into new or existing PPTX files.",
    "developerName": "Mermaid2Pptx Contributors",
    "category": "Productivity",
    "capabilities": ["Write"],
    "defaultPrompt": [
      "Create a PowerPoint deck with editable Mermaid diagrams.",
      "Add a native Mermaid flowchart to an existing PPTX.",
      "Run Mermaid2Pptx visual and XML QA."
    ]
  }
}
```

Set marketplace `interface.displayName` to `Mermaid2Pptx Team` without changing the generated entry.

- [ ] **Step 5: Run the contract test and verify GREEN**

Run:

```powershell
pwsh -NoProfile -File tests/Plugin/PluginContract.Tests.ps1
```

Expected: exit code 0.

- [ ] **Step 6: Commit the scaffold and contract**

```powershell
git add .agents/plugins/marketplace.json plugins/mermaid2pptx-presentations/.codex-plugin/plugin.json tests/Plugin
git commit -m "Add Mermaid2Pptx plugin scaffold"
```

---

### Task 2: Implement Repository Discovery And Automatic Bootstrap

**Files:**
- Create: `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Initialize-Mermaid2Pptx.ps1`
- Create: `tests/Plugin/Environment.Tests.ps1`

**Interfaces:**
- Consumes: `Mermaid2Pptx.sln`, `src/Mermaid2Pptx/Mermaid2Pptx.csproj`, and `src/Mermaid2Pptx.Qa/Mermaid2Pptx.Qa.csproj`.
- Produces: a `PSCustomObject` with `RepoRoot`, `SolutionPath`, `ProjectPath`, `QaProjectPath`, and `PlaywrightScript`.

- [ ] **Step 1: Write failing environment tests**

Create `Environment.Tests.ps1` that:

```powershell
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
Assert-Throws { & $script -RepoRoot (Join-Path $env:TEMP "missing-mermaid2pptx") -SkipBrowserInstall } "Could not locate"
Assert-Throws { & $script -RepoRoot $repo -DotnetCommand "missing-dotnet-command" -SkipBrowserInstall } "dotnet"
```

- [ ] **Step 2: Run the test and verify RED**

Run `pwsh -NoProfile -File tests/Plugin/Environment.Tests.ps1`.

Expected: FAIL with `initializer exists`.

- [ ] **Step 3: Implement the initializer**

Use this parameter contract and domain-specific functions:

```powershell
[CmdletBinding()]
param(
    [string]$RepoRoot = $env:MERMAID2PPTX_REPO,
    [string]$DotnetCommand = "dotnet",
    [switch]$ForceRestore,
    [switch]$SkipBrowserInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-Ancestors {
    param([string]$Start)
    $cursor = [IO.Path]::GetFullPath($Start)
    while ($cursor) {
        $cursor
        $parent = Split-Path -Path $cursor -Parent
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}

function Resolve-Mermaid2PptxRepository {
    param([string]$RequestedRoot)
    $candidates = @()
    if ($RequestedRoot) {
        $candidates += $RequestedRoot
    }
    else {
        $candidates += @(Get-Ancestors -Start (Get-Location).Path)
        $candidates += @(Get-Ancestors -Start $PSScriptRoot)
    }
    foreach ($candidate in $candidates | Select-Object -Unique) {
        $full = [IO.Path]::GetFullPath($candidate)
        if ((Test-Path (Join-Path $full "Mermaid2Pptx.sln")) -and
            (Test-Path (Join-Path $full "src/Mermaid2Pptx/Mermaid2Pptx.csproj"))) {
            return $full
        }
    }
    throw "Could not locate the Mermaid2Pptx repository. Pass -RepoRoot or set MERMAID2PPTX_REPO."
}
```

After resolution:

1. Resolve `$DotnetCommand` with `Get-Command`.
2. Run `dotnet --list-sdks` and require at least one line matching `^8\.`.
3. Run `dotnet restore Mermaid2Pptx.sln` when `project.assets.json` is missing or `-ForceRestore` is set.
4. Run `dotnet build Mermaid2Pptx.sln --no-restore`; rely on MSBuild incremental compilation.
5. Locate `src/Mermaid2Pptx/bin/Debug/net8.0/playwright.ps1`.
6. Unless skipped, invoke that script with `install chromium`.
7. Return the five-path object described in **Interfaces**.
8. Check `$LASTEXITCODE` after every external command and throw a command-specific message.

- [ ] **Step 4: Run the environment tests and verify GREEN**

Run:

```powershell
pwsh -NoProfile -File tests/Plugin/Environment.Tests.ps1
```

Expected: build succeeds, explicit and outside-directory discovery pass, invalid paths fail with the asserted diagnostics.

- [ ] **Step 5: Commit bootstrap**

```powershell
git add plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Initialize-Mermaid2Pptx.ps1 tests/Plugin/Environment.Tests.ps1
git commit -m "Add portable Mermaid2Pptx bootstrap"
```

---

### Task 3: Implement Fast Native-Shape Package Auditing

**Files:**
- Create: `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Test-Mermaid2PptxDeck.ps1`
- Create: `tests/Plugin/DeckAudit.Tests.ps1`

**Interfaces:**
- Consumes: `-Pptx`, optional `-BaselinePptx`, `-DiagramSlides`, and `-Standalone`.
- Produces: one audit result object per checked slide; throws when an invariant fails.

- [ ] **Step 1: Write failing audit tests**

The test must:

1. Generate a standalone deck with `graph TD; A-->B`.
2. Assert standalone audit succeeds.
3. Generate a base deck and insert a second diagram into slide 1.
4. Assert comparison audit reports increased native shapes and unchanged picture/image counts.
5. Copy the standalone file, add a `ppt/media/fake.png` ZIP entry, and assert standalone audit throws `media`.

Use `[IO.Compression.ZipFile]::Open($path, 'Update')` to add the negative fixture; keep all files under a unique directory in `$env:TEMP` and remove that exact directory in `finally`.

- [ ] **Step 2: Run the audit test and verify RED**

Run `pwsh -NoProfile -File tests/Plugin/DeckAudit.Tests.ps1`.

Expected: FAIL because `Test-Mermaid2PptxDeck.ps1` does not exist.

- [ ] **Step 3: Implement ZIP and slide fact extraction**

Implement:

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Pptx,
    [string]$BaselinePptx,
    [int[]]$DiagramSlides,
    [switch]$Standalone
)
```

Create `Get-PptxFacts` that opens the package read-only, reads
`ppt/presentation.xml` and `ppt/_rels/presentation.xml.rels`, resolves ordered
slide targets by relationship id, then records for each slide:

```powershell
[PSCustomObject]@{
    Number = $slideNumber
    Part = $slidePart
    ShapeCount = $slideXml.SelectNodes("//*[local-name()='sp' or local-name()='cxnSp']").Count
    PictureCount = $slideXml.SelectNodes("//*[local-name()='pic']").Count
    ImageRelationshipCount = @($slideRelationships |
        Where-Object { $_.Type -match "/image$" }).Count
}
```

Also record required package parts and all entries beginning with `ppt/media/`.
Dispose the archive in `finally`.

- [ ] **Step 4: Implement invariant comparison**

Apply these exact rules:

- Missing `ppt/presentation.xml` or slide relationship targets: throw.
- Empty `-DiagramSlides`: audit all candidate slides.
- Standalone: every checked slide has `ShapeCount > 0`, every checked slide has
  `PictureCount = 0`, and the package has zero `ppt/media/*` entries.
- Insertion: require `-BaselinePptx`; require candidate `ShapeCount` greater
  than baseline on each target; require equal picture and image relationship
  counts on corresponding target slides.
- Return results only after all targets pass.

- [ ] **Step 5: Run the audit tests and verify GREEN**

Run `pwsh -NoProfile -File tests/Plugin/DeckAudit.Tests.ps1`.

Expected: standalone and insertion checks pass; injected media is rejected.

- [ ] **Step 6: Commit the audit**

```powershell
git add plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Test-Mermaid2PptxDeck.ps1 tests/Plugin/DeckAudit.Tests.ps1
git commit -m "Add Mermaid target slide package audit"
```

---

### Task 4: Implement Atomic Conversion And Insertion

**Files:**
- Create: `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Invoke-Mermaid2Pptx.ps1`
- Create: `tests/Plugin/Invocation.Tests.ps1`

**Interfaces:**
- Consumes: exactly one of `-Mermaid`, `-MermaidFile`, `-MermaidStdin`, `-Html`, or `-SourcePptx`; plus `-Out`, optional `-InsertInto`, `-Map`, `-RepoRoot`, and `-Force`.
- Produces: a validated final PPTX path; preserves source decks and diagnostic candidates.

- [ ] **Step 1: Write failing invocation tests**

Test these behaviors with SHA-256 hashes for source preservation:

```powershell
. "$PSScriptRoot/TestSupport.ps1"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$invoke = Join-Path $repo "plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Invoke-Mermaid2Pptx.ps1"
$work = Join-Path $env:TEMP "mermaid2pptx-invocation-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
$standalone = Join-Path $work "standalone.pptx"
$fromFile = Join-Path $work "from-file.pptx"
$fromStdin = Join-Path $work "from-stdin.pptx"
$fromHtml = Join-Path $work "from-html.pptx"
$inserted = Join-Path $work "inserted.pptx"
$fromSourcePptx = Join-Path $work "from-source-pptx.pptx"
$badMap = Join-Path $work "bad-map.pptx"
$invalidMermaid = Join-Path $work "invalid-mermaid.pptx"

try {
& $invoke -RepoRoot $repo -Mermaid "graph TD; A-->B" -Out $standalone
Assert-True (Test-Path $standalone) "standalone output"

$mmd = Join-Path $work "diagram.mmd"
Set-Content -LiteralPath $mmd -Value "graph LR; FileA-->FileB"
& $invoke -RepoRoot $repo -MermaidFile $mmd -Out $fromFile
"graph TD; StdinA-->StdinB" | & $invoke -RepoRoot $repo -MermaidStdin -Out $fromStdin
& $invoke -RepoRoot $repo -Html (Join-Path $repo "samples/sample.html") -Out $fromHtml
Assert-True (Test-Path $fromFile) "Mermaid file output"
Assert-True (Test-Path $fromStdin) "Mermaid stdin output"
Assert-True (Test-Path $fromHtml) "HTML output"

$baseHash = (Get-FileHash $standalone -Algorithm SHA256).Hash
& $invoke -RepoRoot $repo -Mermaid "graph LR; C-->D" `
    -InsertInto $standalone -Map "1=1" -Out $inserted
Assert-Equal $baseHash (Get-FileHash $standalone -Algorithm SHA256).Hash "source preserved"

& $invoke -RepoRoot $repo -SourcePptx $fromFile `
    -InsertInto $standalone -Map "1=1" -Out $fromSourcePptx
Assert-True (Test-Path $fromSourcePptx) "source PPTX insertion"

Assert-Throws {
    & $invoke -RepoRoot $repo -Mermaid "graph TD; X-->Y" -Out $standalone
} "already exists"

Assert-Throws {
    & $invoke -RepoRoot $repo -Mermaid "graph TD; X-->Y" `
        -InsertInto $standalone -Map "99=1" -Out $badMap
} "slide"
Assert-True (-not (Test-Path $badMap)) "invalid mapping publishes nothing"

Assert-Throws {
    & $invoke -RepoRoot $repo -Mermaid "this is not valid Mermaid syntax" -Out $invalidMermaid
} "Mermaid|convert|render"
Assert-True (-not (Test-Path $invalidMermaid)) "invalid Mermaid publishes nothing"
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP)
    Assert-True ($resolvedWork.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) "temporary work path"
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force
}
```

- [ ] **Step 2: Run the invocation test and verify RED**

Run `pwsh -NoProfile -File tests/Plugin/Invocation.Tests.ps1`.

Expected: FAIL because the invocation adapter does not exist.

- [ ] **Step 3: Implement explicit parameter sets**

Use:

```powershell
[CmdletBinding(DefaultParameterSetName = "MermaidFile")]
param(
    [Parameter(Mandatory, ParameterSetName = "Mermaid")][string]$Mermaid,
    [Parameter(Mandatory, ParameterSetName = "MermaidFile")][string]$MermaidFile,
    [Parameter(Mandatory, ParameterSetName = "MermaidStdin")][switch]$MermaidStdin,
    [Parameter(Mandatory, ParameterSetName = "Html")][string]$Html,
    [Parameter(Mandatory, ParameterSetName = "SourcePptx")][string]$SourcePptx,
    [Parameter(Mandatory)][string]$Out,
    [string]$InsertInto,
    [string]$Map,
    [string]$RepoRoot = $env:MERMAID2PPTX_REPO,
    [string]$SlideSelector = ".slide",
    [string]$SvgSelector = "svg",
    [double]$Width = 13.333,
    [double]$Height = 7.5,
    [switch]$Force
)
```

Validate path existence, disallow `-SourcePptx` without `-InsertInto`, require
`-Map` with insertion, reject output equal to a source path, and reject an
existing output unless `-Force`.

- [ ] **Step 4: Implement candidate execution and audit**

1. Call `Initialize-Mermaid2Pptx.ps1`.
2. Build the exact C# CLI argument array from the active parameter set.
3. Capture stdin to a temporary `.mmd` and pass `--mermaid-file`.
4. Create the candidate beside the destination as
   `.<filename>.<guid>.tmp.pptx`.
5. Invoke `dotnet run --no-build --project <ProjectPath> -- <args>`.
6. For standalone output call `Test-Mermaid2PptxDeck.ps1 -Standalone`.
7. For insertion parse the target side of every `target=source` mapping and
   call the audit with `-BaselinePptx $InsertInto`.
8. On success, remove an existing destination only when `-Force`, then move the
   candidate to the destination.
9. On failure, move an existing candidate to
   `<repo>/out/plugin-diagnostics/<timestamp>-<filename>` and rethrow.
10. Remove only the generated stdin temporary file in `finally`.

- [ ] **Step 5: Run invocation tests and verify GREEN**

Run `pwsh -NoProfile -File tests/Plugin/Invocation.Tests.ps1`.

Expected: standalone and insertion succeed; original hash remains unchanged;
existing output and invalid mapping fail without publishing.

- [ ] **Step 6: Commit invocation**

```powershell
git add plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Invoke-Mermaid2Pptx.ps1 tests/Plugin/Invocation.Tests.ps1
git commit -m "Add atomic Mermaid2Pptx invocation adapter"
```

---

### Task 5: Implement The Optional Full QA Adapter

**Files:**
- Create: `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Invoke-Mermaid2PptxQa.ps1`
- Create: `tests/Plugin/Qa.Tests.ps1`

**Interfaces:**
- Consumes: `-Html`, `-Out`, selectors, dimensions, and optional `-RepoRoot`.
- Produces: the QA output directory containing `report.json`.

- [ ] **Step 1: Write failing QA adapter tests**

Assert a missing HTML file fails before `dotnet`, then run the adapter against
`samples/sample.html` in a unique temporary output directory and require
`report.json`.

- [ ] **Step 2: Run the QA test and verify RED**

Run `pwsh -NoProfile -File tests/Plugin/Qa.Tests.ps1`.

Expected: FAIL because the QA adapter does not exist.

- [ ] **Step 3: Implement the QA adapter**

Use parameters matching the approved defaults:

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Html,
    [Parameter(Mandatory)][string]$Out,
    [string]$RepoRoot = $env:MERMAID2PPTX_REPO,
    [string]$SlideSelector = ".slide",
    [string]$SvgSelector = "svg",
    [double]$Width = 13.333,
    [double]$Height = 7.5
)
```

Resolve inputs, call the initializer, run
`dotnet run --no-build --project <QaProjectPath> -- --html ...`, check the exit
code, require `<Out>/report.json`, and return the resolved output directory.

- [ ] **Step 4: Run the QA test and verify GREEN**

Run `pwsh -NoProfile -File tests/Plugin/Qa.Tests.ps1`.

Expected: invalid input fails early; the sample produces `report.json`.

- [ ] **Step 5: Commit QA adapter**

```powershell
git add plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Invoke-Mermaid2PptxQa.ps1 tests/Plugin/Qa.Tests.ps1
git commit -m "Add Mermaid2Pptx full QA adapter"
```

---

### Task 6: Author And Forward-Test The Agent Skill

**Files:**
- Create: `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/SKILL.md`
- Create: `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/agents/openai.yaml`
- Create: `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/references/cli-and-qa.md`
- Create: `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/references/native-shape-invariants.md`
- Create: `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/references/presentations-integration.md`
- Modify: `tests/Plugin/PluginContract.Tests.ps1`

**Interfaces:**
- Consumes: the four tested scripts from Tasks 2–5 and the installed `Presentations` skill.
- Produces: one discoverable skill whose default prompt explicitly uses `$mermaid2pptx-presentations`.

- [ ] **Step 1: Run the pre-change baseline scenario**

Use a fresh subagent with the current personal skill and this exact task:

```text
Use the existing mermaid2pptx-presentations skill at
C:\Users\User\.codex\skills\mermaid2pptx-presentations to describe the exact
commands an agent should run after a teammate clones Mermaid2Pptx to
C:\team\diagram-tools\Mermaid2PPTX, starts Codex outside that repo, has no prior
build output, and wants a template-based PPTX with a native Mermaid flowchart.
Include setup, insertion, and the default post-run audit.
```

Record whether it uses a fixed path fallback, requires manual setup, or lacks a
fast baseline-comparison audit. Those observed failures are the RED evidence.

- [ ] **Step 2: Write the minimal skill**

Use this frontmatter:

```yaml
---
name: mermaid2pptx-presentations
description: Use when creating or editing PowerPoint PPTX decks that need editable Mermaid diagrams, native DrawingML flowcharts, Mermaid insertion into existing templates, or Mermaid2Pptx conversion and QA.
---
```

The body must:

1. Route narrative, layout, templates, and ordinary slides to `Presentations`.
2. Route Mermaid diagrams to the bundled scripts.
3. Require requirements for audience, output, source deck, diagrams, target
   slides, and acceptance criteria before production.
4. Define new-deck and template-deck order.
5. Use fast audit by default and full QA for explicit/high-risk runs.
6. Preserve original decks and use final PPTX files rather than raw DrawingML
   as the exchange format.
7. For multiple diagrams, chain outputs only through a scratch directory and
   reserve the user-requested final path for the last successful insertion.
8. Link directly to each reference and state when to read it.

- [ ] **Step 3: Add focused references and UI metadata**

Keep `SKILL.md` concise. Put CLI flags and examples in `cli-and-qa.md`, native
shape/package rules in `native-shape-invariants.md`, and new/template deck
coordination in `presentations-integration.md`.

Create `agents/openai.yaml`:

```yaml
interface:
  display_name: "Mermaid2Pptx Presentations"
  short_description: "Build PPTX decks with editable Mermaid diagrams"
  default_prompt: "Use $mermaid2pptx-presentations to build a polished PowerPoint deck with editable native Mermaid diagrams."
```

- [ ] **Step 4: Extend the contract test**

Assert every skill file and script exists, `SKILL.md` contains no
`E:\project\Mermaid2PPTX`, and no `bin`, `obj`, `.pptx`, or `ppt/media` artifact
exists beneath the plugin root.

- [ ] **Step 5: Validate the skill and plugin**

Create a disposable validator environment under ignored `out/`:

```powershell
py -3 -m venv out/plugin-validator-venv
& out/plugin-validator-venv/Scripts/python.exe -m pip install PyYAML
& out/plugin-validator-venv/Scripts/python.exe `
  C:/Users/User/.codex/skills/.system/skill-creator/scripts/quick_validate.py `
  plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations
& out/plugin-validator-venv/Scripts/python.exe `
  C:/Users/User/.codex/skills/.system/plugin-creator/scripts/validate_plugin.py `
  plugins/mermaid2pptx-presentations
pwsh -NoProfile -File tests/Plugin/Run-All.ps1
```

Expected: both validators and all plugin tests pass.

- [ ] **Step 6: Run the GREEN forward tests**

Dispatch fresh agents with the new skill path for:

1. a new five-slide technical presentation with one Mermaid flowchart;
2. a supplied-template edit with two mapped Mermaid diagrams.

Require each agent to identify `Presentations` as base-deck owner, invoke the
bundled initializer/invoker, preserve the template, and perform target-slide
audit. If either agent omits an observed baseline requirement, tighten only the
relevant skill instruction and rerun that scenario.

- [ ] **Step 7: Commit the verified skill**

```powershell
git add plugins/mermaid2pptx-presentations/skills tests/Plugin/PluginContract.Tests.ps1
git commit -m "Add Mermaid2Pptx presentation workflow skill"
```

---

### Task 7: Install And Run End-To-End Acceptance

**Files:**
- Modify only if failures prove a gap: plugin files or `tests/Plugin/*.Tests.ps1`
- Generate only under ignored `out/plugin-e2e/`

**Interfaces:**
- Consumes: complete repository plugin and local marketplace.
- Produces: installed plugin plus verified new-deck, template-deck, and QA evidence.

- [ ] **Step 1: Run the complete repository verification**

```powershell
pwsh -NoProfile -File tests/Plugin/Run-All.ps1
dotnet test tests/Mermaid2Pptx.Tests/Mermaid2Pptx.Tests.csproj
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- `
  --mermaid "graph TD; A-->B" `
  --out out/plugin-e2e/basic-native.pptx
```

Expected: plugin tests pass, xUnit reports zero failures, and the CLI creates the deck.

- [ ] **Step 2: Validate schemas again with fresh output**

Run both Python validators from Task 6 and require exit code 0.

- [ ] **Step 3: Install the repository marketplace and plugin**

From the current repository root:

```powershell
$repo = (Resolve-Path .).Path
codex plugin marketplace add $repo
codex plugin add mermaid2pptx-presentations@mermaid2pptx-team
codex plugin list
```

Expected: the marketplace and plugin appear under `mermaid2pptx-team`. If the
Codex desktop package denies CLI execution, record the exact access error and
provide the validated plugin plus the manual Codex app installation handoff;
do not claim installation succeeded.

- [ ] **Step 4: Verify a relocated clone**

Create a temporary local clone, run the copied initializer from outside that
clone, then remove only that verified temporary clone:

```powershell
$relocated = Join-Path $env:TEMP "mermaid2pptx-plugin-relocated-$([guid]::NewGuid().ToString('N'))"
git clone --local . $relocated
Push-Location $env:TEMP
try {
    & "$relocated/plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Initialize-Mermaid2Pptx.ps1" `
      -RepoRoot $relocated `
      -SkipBrowserInstall
}
finally {
    Pop-Location
    $resolvedRelocated = [IO.Path]::GetFullPath($relocated)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP)
    if (-not $resolvedRelocated.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove non-temporary relocation path: $resolvedRelocated"
    }
    Remove-Item -LiteralPath $resolvedRelocated -Recurse -Force
}
```

Expected: the copied initializer resolves and builds the copied repository
without referring to the original checkout.

- [ ] **Step 5: Run new-deck acceptance**

In a fresh agent context, use the installed plugin to create a small technical
deck under `out/plugin-e2e/new-deck/` with one Mermaid flowchart. Require:

- a final PPTX;
- target-slide fast audit pass;
- rendered slide inspection through `Presentations`;
- no converted-diagram media additions.

- [ ] **Step 6: Run template-deck acceptance**

Use a copy of a repository sample PPTX as the source template, insert two
Mermaid diagrams into explicit target slides, and verify:

- source SHA-256 is unchanged;
- output is a separate PPTX;
- both target slide audits pass;
- the template hierarchy/fidelity checks required by `Presentations` pass.

- [ ] **Step 7: Run optional full QA acceptance**

```powershell
pwsh -NoProfile -File `
  plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Invoke-Mermaid2PptxQa.ps1 `
  -RepoRoot . `
  -Html samples/all-diagrams.html `
  -Out out/plugin-e2e/full-qa
```

Expected: `report.json` exists and `xmlAuditFailures` is zero.

- [ ] **Step 8: Verify the final diff and commit proven fixes**

Run:

```powershell
git diff --check
git status --short
git log --oneline -8
```

Commit only fixes that were required by acceptance failures. Keep
`out/plugin-e2e/`, `outputs/`, `bin/`, and `obj/` untracked or ignored.

- [ ] **Step 9: Hand off the plugin**

Report:

- validator results;
- plugin test and xUnit counts;
- conversion and QA evidence;
- whether local installation succeeded;
- the exact repository marketplace path;
- Codex `View` and `Share` deep links required by `plugin-creator`.
