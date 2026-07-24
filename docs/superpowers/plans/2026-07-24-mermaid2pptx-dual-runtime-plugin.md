# Mermaid2Pptx Dual-Runtime Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the existing Codex plugin so the same skill and scripts also install and run as a Claude Code plugin, letting an agent build a `.pptx` deck and insert editable native-shape Mermaid flowcharts.

**Architecture:** One plugin directory (`plugins/mermaid2pptx-presentations/`) carries two manifests — the existing `.codex-plugin/plugin.json` and a new `.claude-plugin/plugin.json` — over one shared `skills/` tree and one shared set of PowerShell scripts. A new repo-root `.claude-plugin/marketplace.json` mirrors the existing Codex marketplace. The single `SKILL.md` is generalized so narrative slides route to each host's native presentation skill (`anthropic-skills:pptx` on Claude Code, `presentations:Presentations` on Codex); Mermaid diagrams stay native DrawingML via the unchanged .NET converter.

**Tech Stack:** Claude Code plugin manifest + marketplace JSON, Markdown skill/command files, PowerShell 7 wrapper scripts (reused), .NET 8 converter (reused), Open XML `.pptx`.

## Global Constraints

- Normalized plugin name is `mermaid2pptx-presentations` in every manifest, marketplace entry, and path.
- Claude Code marketplace name mirrors the Codex one: `mermaid2pptx-team`.
- No plugin file may contain a user-specific absolute path (e.g. `E:\project\...`).
- Do not modify converter source (`src/**`), tests (`tests/**`), the PowerShell scripts (`scripts/*.ps1`), the Codex manifest (`.codex-plugin/plugin.json`), the Codex agent file (`agents/openai.yaml`), or the Codex marketplace (`.agents/plugins/marketplace.json`). They are reused as-is; the Codex plugin must not regress.
- Never commit generated `.pptx`, QA output, Playwright browsers, or build artifacts. All end-to-end output goes under `out/` (already git-ignored) — verify with `git status` before every commit.
- Native-shape invariant: Mermaid diagram slides contain `p:sp`/`p:cxnSp` and no `p:pic` or `ppt/media/*`.
- Environment fact: local `dotnet` has .NET 8 SDK `8.0.128` and runtime `8.0.28` (plus SDK 10.0.301). The `net8.0` converter builds and runs without a roll-forward setting; do not add `global.json` or change target frameworks.
- Every commit message ends with the trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

### Task 1: Claude Code plugin manifest

Add `.claude-plugin/plugin.json` next to the existing `.codex-plugin/plugin.json` in the same plugin directory. Claude Code reads only `.claude-plugin/plugin.json` and ignores `.codex-plugin/`, so both coexist over the shared `skills/` tree. Claude Code auto-discovers the plugin-root `skills/` and `commands/` directories, so the manifest stays metadata-only (no component-path fields).

**Files:**
- Create: `plugins/mermaid2pptx-presentations/.claude-plugin/plugin.json`

**Interfaces:**
- Consumes: nothing.
- Produces: an installable Claude Code plugin manifest named `mermaid2pptx-presentations`, consumed by the marketplace in Task 2.

- [ ] **Step 1: Confirm the manifest is absent (baseline)**

Run:
```bash
test -f plugins/mermaid2pptx-presentations/.claude-plugin/plugin.json && echo EXISTS || echo ABSENT
```
Expected: `ABSENT`

- [ ] **Step 2: Create the manifest**

Create `plugins/mermaid2pptx-presentations/.claude-plugin/plugin.json` with exactly:
```json
{
  "name": "mermaid2pptx-presentations",
  "version": "0.1.0",
  "description": "Build PowerPoint decks and insert editable native-shape Mermaid diagrams with Mermaid2Pptx.",
  "author": {
    "name": "Mermaid2Pptx Contributors"
  },
  "keywords": [
    "powerpoint",
    "pptx",
    "mermaid",
    "drawingml",
    "presentations"
  ]
}
```

- [ ] **Step 3: Verify it is valid JSON with the required name**

Run:
```bash
pwsh -NoProfile -Command "$m = Get-Content -Raw plugins/mermaid2pptx-presentations/.claude-plugin/plugin.json | ConvertFrom-Json; if ($m.name -ne 'mermaid2pptx-presentations') { throw 'wrong name' }; 'OK: ' + $m.name"
```
Expected: `OK: mermaid2pptx-presentations`

- [ ] **Step 4: (Optional) Validate with the Claude CLI if present**

Run:
```bash
command -v claude >/dev/null 2>&1 && claude plugin validate plugins/mermaid2pptx-presentations || echo "claude CLI not available; JSON check in Step 3 is authoritative"
```
Expected: validation success, or the "not available" fallback message. A validation failure here is a real failure — fix the manifest before continuing.

- [ ] **Step 5: Confirm no stray artifacts, then commit**

Run:
```bash
git status --short
```
Expected: only `plugins/mermaid2pptx-presentations/.claude-plugin/plugin.json` is new.

```bash
git add plugins/mermaid2pptx-presentations/.claude-plugin/plugin.json
git commit -m "$(printf 'Add Claude Code plugin manifest for mermaid2pptx-presentations\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 2: Repo-root Claude Code marketplace

Add `.claude-plugin/marketplace.json` at the repository root so a teammate can `add` the local marketplace and install the plugin. The `source` is a plain relative string resolved from the marketplace root (the repo root).

**Files:**
- Create: `.claude-plugin/marketplace.json`

**Interfaces:**
- Consumes: the plugin manifest from Task 1 (plugin `name` must match `mermaid2pptx-presentations`).
- Produces: a local marketplace named `mermaid2pptx-team` exposing one plugin at `./plugins/mermaid2pptx-presentations`.

- [ ] **Step 1: Confirm the marketplace is absent (baseline)**

Run:
```bash
test -f .claude-plugin/marketplace.json && echo EXISTS || echo ABSENT
```
Expected: `ABSENT`

- [ ] **Step 2: Create the marketplace**

Create `.claude-plugin/marketplace.json` at the repo root with exactly:
```json
{
  "name": "mermaid2pptx-team",
  "owner": {
    "name": "Mermaid2Pptx Contributors"
  },
  "metadata": {
    "description": "Local marketplace for the Mermaid2Pptx presentations plugin.",
    "version": "0.1.0"
  },
  "plugins": [
    {
      "name": "mermaid2pptx-presentations",
      "source": "./plugins/mermaid2pptx-presentations",
      "description": "Build PowerPoint decks and insert editable native-shape Mermaid diagrams with Mermaid2Pptx.",
      "version": "0.1.0",
      "category": "productivity"
    }
  ]
}
```

- [ ] **Step 3: Verify JSON validity and that the source path exists**

Run:
```bash
pwsh -NoProfile -Command "$m = Get-Content -Raw .claude-plugin/marketplace.json | ConvertFrom-Json; if ($m.name -ne 'mermaid2pptx-team') { throw 'wrong marketplace name' }; $p = $m.plugins[0]; if ($p.name -ne 'mermaid2pptx-presentations') { throw 'wrong plugin name' }; if (-not (Test-Path $p.source)) { throw 'source path missing: ' + $p.source }; 'OK: ' + $p.name + ' -> ' + $p.source"
```
Expected: `OK: mermaid2pptx-presentations -> ./plugins/mermaid2pptx-presentations`

- [ ] **Step 4: (Optional) Validate the marketplace with the Claude CLI if present**

Run:
```bash
command -v claude >/dev/null 2>&1 && claude plugin validate --marketplace .claude-plugin/marketplace.json || echo "claude CLI not available; JSON check in Step 3 is authoritative"
```
Expected: validation success, or the fallback message. (If this exact flag is unsupported by the installed CLI, the Step 3 check remains authoritative; do not block on CLI flag differences.)

- [ ] **Step 5: Commit**

Run:
```bash
git status --short
```
Expected: only `.claude-plugin/marketplace.json` is new.

```bash
git add .claude-plugin/marketplace.json
git commit -m "$(printf 'Add Claude Code marketplace for mermaid2pptx-presentations\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 3: Make the shared skill runtime-neutral

Generalize `SKILL.md` so every reference to the host presentation skill names both runtimes, and add a one-line host note to the deep reference. This is the only substantive content edit; scripts and other references are unchanged.

**Files:**
- Modify: `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/SKILL.md`
- Modify: `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/references/presentations-integration.md`

**Interfaces:**
- Consumes: nothing new.
- Produces: a skill that instructs agents to use `anthropic-skills:pptx` (Claude Code) or `presentations:Presentations` (Codex) for narrative slides, unchanged for all diagram/QA/insertion workflow.

- [ ] **Step 1: Show the current references (baseline)**

Run:
```bash
grep -n "presentations:Presentations" plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/SKILL.md
```
Expected: five matches (Overview, REQUIRED SUB-SKILL, Workflow step 2, Workflow step 5, Quick reference table).

- [ ] **Step 2: Edit the Overview paragraph**

In `SKILL.md`, replace:
```text
Build the deck with `presentations:Presentations`; convert only Mermaid diagrams
with this skill's scripts. Exchange complete `.pptx` files between the two
workflows so every converted diagram remains editable native DrawingML.
```
with:
```text
Build the deck with the host's native presentation skill; convert only Mermaid
diagrams with this skill's scripts. Exchange complete `.pptx` files between the
two workflows so every converted diagram remains editable native DrawingML.
```

- [ ] **Step 3: Edit the REQUIRED SUB-SKILL block**

In `SKILL.md`, replace:
```text
**REQUIRED SUB-SKILL:** Use `presentations:Presentations` for narrative,
ordinary slides, layout, template fidelity, and deck-level visual review.
```
with:
```text
**REQUIRED SUB-SKILL:** Use the host's native presentation skill for narrative,
ordinary slides, layout, template fidelity, and deck-level visual review:

- Claude Code: `anthropic-skills:pptx`
- OpenAI Codex: `presentations:Presentations`

The rest of this skill refers to it as "the host presentation skill".
```

- [ ] **Step 4: Edit Workflow step 2**

In `SKILL.md`, replace:
```text
2. Create the ordinary base deck with `presentations:Presentations`, writing it
   to a scratch path. For a supplied template, keep the source immutable and
   have Presentations prepare the diagram targets on a scratch working copy.
   Use that prepared copy as the first insertion source.
```
with:
```text
2. Create the ordinary base deck with the host presentation skill, writing it
   to a scratch path. For a supplied template, keep the source immutable and
   have that skill prepare the diagram targets on a scratch working copy.
   Use that prepared copy as the first insertion source.
```

- [ ] **Step 5: Edit Workflow step 5**

In `SKILL.md`, replace:
```text
5. Perform deck-level visual review with `presentations:Presentations`. Run
```
with:
```text
5. Perform deck-level visual review with the host presentation skill. Run
```

- [ ] **Step 6: Edit the Quick reference table row**

In `SKILL.md`, replace:
```text
| Story, theme, ordinary slides, template fidelity | `presentations:Presentations` |
```
with:
```text
| Story, theme, ordinary slides, template fidelity | Host presentation skill (`anthropic-skills:pptx` / `presentations:Presentations`) |
```

- [ ] **Step 7: Add the host note to the deep reference**

In `references/presentations-integration.md`, replace the first line:
```text
# Presentations integration
```
with:
```text
# Presentations integration

> **Host skill:** In this document, `presentations:Presentations` names the
> host's native presentation skill. On Claude Code it is `anthropic-skills:pptx`;
> on OpenAI Codex it is `presentations:Presentations`.
```

- [ ] **Step 8: Verify both runtimes are named and the frontmatter is intact**

Run:
```bash
grep -n "anthropic-skills:pptx" plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/SKILL.md plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/references/presentations-integration.md
head -4 plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/SKILL.md
```
Expected: `anthropic-skills:pptx` appears in both files (SKILL.md at least twice: the sub-skill block and the table); the `head` output still shows the intact YAML frontmatter (`---`, `name: mermaid2pptx-presentations`, `description: ...`, `---`).

- [ ] **Step 9: Commit**

Run:
```bash
git status --short
```
Expected: only the two Markdown files are modified.

```bash
git add plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/SKILL.md plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/references/presentations-integration.md
git commit -m "$(printf 'Make mermaid2pptx skill host-neutral for Claude Code and Codex\n\nName anthropic-skills:pptx (Claude Code) and presentations:Presentations\n(Codex) as the host presentation skill.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 4: Add the `/mermaid-deck` slash command

Add a convenience Claude Code command that starts the workflow via the skill. Claude Code auto-discovers plugin-root `commands/*.md`; Codex ignores the directory.

**Files:**
- Create: `plugins/mermaid2pptx-presentations/commands/mermaid-deck.md`

**Interfaces:**
- Consumes: the `mermaid2pptx-presentations` skill (Task 3) at runtime.
- Produces: a `/mermaid-deck` command (invoked as `/mermaid-deck` or `/mermaid2pptx-presentations:mermaid-deck`).

- [ ] **Step 1: Confirm the command is absent (baseline)**

Run:
```bash
test -f plugins/mermaid2pptx-presentations/commands/mermaid-deck.md && echo EXISTS || echo ABSENT
```
Expected: `ABSENT`

- [ ] **Step 2: Create the command file**

Create `plugins/mermaid2pptx-presentations/commands/mermaid-deck.md` with exactly:
```markdown
---
description: Build a PowerPoint deck with editable native Mermaid diagrams
argument-hint: [what the deck is about]
---

Use the `mermaid2pptx-presentations` skill to build a PowerPoint `.pptx` deck.

Deck request: $ARGUMENTS

Follow the skill's production contract before producing files: confirm the
audience and communication goal, the requested final `.pptx` path, whether this
is a new deck or an existing template, the Mermaid source and target slide for
each diagram, and the acceptance criteria.

Build the narrative and ordinary slides with the host presentation skill
(`anthropic-skills:pptx` on Claude Code), then insert every diagram as editable
native DrawingML with the skill's `scripts/Invoke-Mermaid2Pptx.ps1`. Keep any
source template immutable and write the requested final path only after every
insertion and the fast native-shape audit succeed.
```

- [ ] **Step 3: Verify the file and its frontmatter**

Run:
```bash
head -4 plugins/mermaid2pptx-presentations/commands/mermaid-deck.md
```
Expected: the first line is `---`, followed by the `description:` and `argument-hint:` keys and the closing `---`.

- [ ] **Step 4: Commit**

Run:
```bash
git status --short
```
Expected: only `plugins/mermaid2pptx-presentations/commands/mermaid-deck.md` is new.

```bash
git add plugins/mermaid2pptx-presentations/commands/mermaid-deck.md
git commit -m "$(printf 'Add /mermaid-deck Claude Code command\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 5: End-to-end verification through the plugin scripts

Prove that the plugin produces a `.pptx` whose Mermaid flowchart is editable native shapes, and that insertion into a base deck passes the fast audit — using the actual reused wrapper scripts. This is the deterministic gate for "an agent can use this plugin to make a pptx with a Mermaid flowchart." No files are created under version control here; all output goes to git-ignored `out/`.

**Files:**
- Test output only: `out/plugin-e2e/*.pptx` (git-ignored; not committed).

**Interfaces:**
- Consumes: `scripts/Invoke-Mermaid2Pptx.ps1` (reused), `scripts/Initialize-Mermaid2Pptx.ps1` (reused), the .NET converter.
- Produces: verification evidence only.

- [ ] **Step 1: Converter regression — run the existing unit tests**

Run (first run also restores/builds; expect it to take a few minutes):
```bash
dotnet test tests/Mermaid2Pptx.Tests/Mermaid2Pptx.Tests.csproj
```
Expected: `Passed!` with 0 failed. If the build fails because the .NET 8 targeting pack is missing, run `dotnet restore Mermaid2Pptx.sln` first, then re-run; do not change the target framework.

- [ ] **Step 2: Prepare the skill-root path variable**

Run:
```bash
pwsh -NoProfile -Command "$env:SKILLROOT='plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations'; New-Item -ItemType Directory -Force out/plugin-e2e | Out-Null; Test-Path (Join-Path $env:SKILLROOT 'scripts/Invoke-Mermaid2Pptx.ps1')"
```
Expected: `True`

- [ ] **Step 3: Build a standalone native-shape flowchart deck via the plugin script**

Run (bootstraps .NET + Playwright on first use):
```bash
pwsh -NoProfile -Command "& 'plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Invoke-Mermaid2Pptx.ps1' -RepoRoot (Get-Location).Path -Mermaid 'flowchart LR; A[Request]-->B{Approved?}; B-->|Yes|C[Ship]; B-->|No|A' -Out 'out/plugin-e2e/flowchart.pptx' -Force"
```
Expected: converter output ending with a line like `Validated Mermaid slides: 1`, and `out/plugin-e2e/flowchart.pptx` is written. (The script throws and preserves a diagnostic candidate under `out/plugin-diagnostics/` on any failure.)

- [ ] **Step 4: Independently assert the native-shape invariants on the output**

Run:
```bash
pwsh -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; $zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path 'out/plugin-e2e/flowchart.pptx')); try { $media=@($zip.Entries | Where-Object { $_.FullName -like 'ppt/media/*' }); $slideEntry=$zip.Entries | Where-Object { $_.FullName -eq 'ppt/slides/slide1.xml' }; $r=[IO.StreamReader]::new($slideEntry.Open()); $xml=$r.ReadToEnd(); $r.Dispose(); if ($media.Count -ne 0) { throw 'FAIL: media parts present' }; if ($xml -match '<p:pic\b') { throw 'FAIL: p:pic present' }; if ($xml -notmatch '<p:(sp|cxnSp)\b') { throw 'FAIL: no native shapes/connectors' }; 'PASS: native-shape flowchart, no media, no p:pic' } finally { $zip.Dispose() }"
```
Expected: `PASS: native-shape flowchart, no media, no p:pic`

- [ ] **Step 5: Build a base deck and insert a flowchart into it (insert + baseline audit path)**

Run:
```bash
pwsh -NoProfile -Command "$s='plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/Invoke-Mermaid2Pptx.ps1'; & $s -RepoRoot (Get-Location).Path -Mermaid 'graph TD; Start-->Finish' -Out 'out/plugin-e2e/base.pptx' -Force; & $s -RepoRoot (Get-Location).Path -Mermaid 'flowchart LR; A[Draft]-->B{Review}; B-->|OK|C[Publish]; B-->|Redo|A' -InsertInto 'out/plugin-e2e/base.pptx' -Map '1=1' -Out 'out/plugin-e2e/final.pptx' -Force"
```
Expected: both invocations report validated Mermaid slides; `out/plugin-e2e/final.pptx` exists and `out/plugin-e2e/base.pptx` is unchanged (the wrapper preserves the insertion source).

- [ ] **Step 6: Confirm no test artifacts leaked into version control**

Run:
```bash
git status --short
```
Expected: empty (all output is under git-ignored `out/`). If anything under `out/` appears, it means an ignore rule is missing — stop and fix `.gitignore` rather than committing artifacts.

- [ ] **Step 7: (Agent-driven, recommended) Real narrative deck + inserted flowchart**

This step is performed by the executing agent, not a single fixed command. Using the Claude Code path:
1. Invoke `anthropic-skills:pptx` to build a small 2-slide narrative base deck at `out/plugin-e2e/story.pptx` (title slide + one content slide reserved for a diagram).
2. Insert a Mermaid flowchart into the reserved slide with `Invoke-Mermaid2Pptx.ps1 -InsertInto out/plugin-e2e/story.pptx -Map "<targetSlide>=1" -Out out/plugin-e2e/story-final.pptx`.
3. Re-run the Step 4 invariant assertion against the inserted slide's XML (adjust `slide1.xml` to the target slide's part).

Success criterion: `out/plugin-e2e/story-final.pptx` opens as a normal deck whose diagram slide contains editable shapes and no image parts. There is nothing to commit in this task.

---

### Task 6: Document installation and confirm no Codex regression

Document how a Claude Code user installs the plugin, and verify the Codex plugin artifacts are byte-for-byte unchanged.

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: the manifest and marketplace from Tasks 1–2.
- Produces: user-facing install instructions; a regression check on the Codex plugin.

- [ ] **Step 1: Verify the Codex plugin files were not modified by this branch**

Run:
```bash
git diff --name-only master -- plugins/mermaid2pptx-presentations/.codex-plugin plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/agents .agents/plugins/marketplace.json
```
Expected: empty output (none of the Codex-owned files changed). If this branch was made from `master`, compare against the pre-work commit instead; the required result is that no script, Codex manifest, agent file, or Codex marketplace appears.

- [ ] **Step 2: Add a Claude Code install section to the README**

In `README.md`, add the following section immediately before the `## Test` section:
```markdown
## Claude Code plugin

This repository is also a local Claude Code plugin marketplace. Install the
`mermaid2pptx-presentations` plugin from a Claude Code session started in the
repository root:

```text
/plugin marketplace add .
/plugin install mermaid2pptx-presentations@mermaid2pptx-team
/reload-plugins
```

After install, the `mermaid2pptx-presentations` skill and the `/mermaid-deck`
command are available. The skill builds narrative slides with
`anthropic-skills:pptx` and inserts editable native-shape Mermaid diagrams with
the Mermaid2Pptx converter. The same plugin directory also ships an OpenAI Codex
manifest (`.codex-plugin/plugin.json`) over the shared skill and scripts.
```

- [ ] **Step 3: Verify the README renders the section and the fenced blocks are balanced**

Run:
```bash
grep -n "Claude Code plugin" README.md
pwsh -NoProfile -Command "$c = Get-Content -Raw README.md; $ticks = ([regex]::Matches($c, '(?m)^```')).Count; if ($ticks % 2 -ne 0) { throw 'unbalanced code fences' }; 'OK: fences balanced (' + $ticks + ')'"
```
Expected: the grep shows the new heading; the fence check prints `OK: fences balanced (<even number>)`.

- [ ] **Step 4: Commit**

Run:
```bash
git status --short
```
Expected: only `README.md` is modified.

```bash
git add README.md
git commit -m "$(printf 'Document Claude Code plugin installation\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

- [ ] **Step 5: Final review of the whole change**

Run:
```bash
git log --oneline -6
git diff --stat master
```
Expected: five focused commits from this plan (Tasks 1–4 and 6; Task 5 has no commit) sitting on top of the spec commit; the diff touches only six files — `.claude-plugin/marketplace.json`, `plugins/mermaid2pptx-presentations/.claude-plugin/plugin.json`, `plugins/mermaid2pptx-presentations/commands/mermaid-deck.md`, the two skill Markdown files (`SKILL.md`, `references/presentations-integration.md`), and `README.md`. No converter, test, script, or Codex-manifest files appear.

---

## Notes for the executor

- First script/test run downloads Playwright Chromium and restores NuGet packages; this is expected and cached afterward (a `.cache/` directory already exists in the repo).
- If `Invoke-Mermaid2Pptx.ps1` fails, read the preserved candidate path it prints under `out/plugin-diagnostics/`; do not publish diagnostics.
- Do not "fix" the Codex manifest, scripts, or converter to make Claude Code happy — the design requires them unchanged and shared. If a genuine incompatibility appears, stop and revisit the spec (`docs/superpowers/specs/2026-07-24-mermaid2pptx-dual-runtime-plugin-design.md`).
