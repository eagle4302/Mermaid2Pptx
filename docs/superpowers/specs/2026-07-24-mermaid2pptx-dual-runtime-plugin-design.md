# Mermaid2Pptx Dual-Runtime Plugin Design

Date: 2026-07-24

## Goal

Package Mermaid2Pptx as a plugin that an agent can use to build a complete
PowerPoint `.pptx` deck and, whenever a diagram is needed, convert Mermaid into
an editable native DrawingML flowchart inside that deck.

The repository already ships a Codex-only plugin
(`plugins/mermaid2pptx-presentations/`) plus a mature .NET converter. This design
extends that plugin so the **same** skill and scripts also work as a **Claude
Code** plugin, without duplicating the substantive plugin content.

Scope decisions confirmed during brainstorming:

- **Runtimes:** support **both** Claude Code and OpenAI Codex from one plugin.
- **Narrative slides:** reuse each runtime's native presentation skill for
  ordinary/narrative slides, layout, and template fidelity; this plugin only
  routes work and inserts editable Mermaid diagrams. It does not reimplement deck
  building.
- **Structure:** one plugin directory, dual manifests, shared `skills/` tree
  (Approach A below).

## Current State

- `src/Mermaid2Pptx/` — .NET 8 CLI/library: Mermaid render (Playwright) → SVG
  parse → native DrawingML mapping → `.pptx`, including `--insert-into` for
  existing decks. Never rasterizes; never emits `ppt/media/*` or `p:pic`.
- `src/Mermaid2Pptx.Qa/` — visual + XML QA.
- `tests/Mermaid2Pptx.Tests/` — native-shape and package invariants.
- `plugins/mermaid2pptx-presentations/` — existing **Codex** plugin:
  - `.codex-plugin/plugin.json`
  - `skills/mermaid2pptx-presentations/` with `SKILL.md`, `references/`,
    `scripts/*.ps1`, and Codex-only `agents/openai.yaml`.
- `.agents/plugins/marketplace.json` — Codex marketplace entry.
- The existing `SKILL.md` hard-requires the Codex skill `presentations:Presentations`.

The PowerShell scripts (`Initialize`, `Invoke`, `Test`, `Publish`, `Qa`) only
prepare the environment and call the .NET CLI. They are **runtime-agnostic** and
are reused as-is.

## Architecture Decision

**Approach A — one plugin directory, dual manifests, one shared skill tree.**

Place a Claude Code manifest next to the existing Codex manifest inside the same
plugin directory; both manifests reference the same `skills/` tree. Add a
repo-root Claude Code marketplace file mirroring the existing Codex marketplace.
Generalize the single `SKILL.md` so its one runtime-specific line (the required
presentation sub-skill) names the correct skill per host.

This keeps the skill, references, and PowerShell scripts as a single source of
truth. Only two small manifests and one marketplace file are added, plus a
minimal optional slash command for Claude Code ergonomics.

### Rejected Alternatives

- **Separate Claude Code plugin directory** referencing the Codex plugin's
  scripts by relative path: fragile cross-directory paths and two `SKILL.md`
  copies that drift. Rejected.
- **Claude Code project-level `.claude/skills/` only** (no plugin manifest):
  usable in this repo immediately but not a distributable plugin, and a real
  plugin still needs the manifest. Rejected as the primary form; the plugin
  manifest supersedes it.
- **Local MCP server** exposing structured tools: adds server lifecycle and
  cross-platform failure modes without improving the CLI conversion model. Kept
  out of scope, consistent with the original plugin design.

## Target Structure

`NEW` marks files added by this design. Everything else is reused unchanged
except two localized edits: `SKILL.md` (runtime-neutral sub-skill reference) and
a one-line host note in `references/presentations-integration.md`.

```text
plugins/mermaid2pptx-presentations/
  .codex-plugin/plugin.json              # reused (Codex manifest)
  .claude-plugin/plugin.json             # NEW (Claude Code manifest)
  commands/
    mermaid-deck.md                      # NEW (optional Claude Code slash command)
  skills/
    mermaid2pptx-presentations/
      SKILL.md                           # edited: runtime-neutral sub-skill reference
      references/
        cli-and-qa.md                    # reused
        native-shape-invariants.md       # reused
        presentations-integration.md     # reused (+ note both host skills)
      scripts/
        Initialize-Mermaid2Pptx.ps1      # reused
        Invoke-Mermaid2Pptx.ps1          # reused
        Test-Mermaid2PptxDeck.ps1        # reused
        Publish-Mermaid2PptxCandidate.ps1# reused
        Invoke-Mermaid2PptxQa.ps1        # reused
      agents/
        openai.yaml                      # reused (Codex-only; ignored by Claude Code)
.claude-plugin/
  marketplace.json                       # NEW (Claude Code marketplace)
.agents/
  plugins/marketplace.json               # reused (Codex marketplace)
```

Both manifests and the marketplace entries use the normalized name
`mermaid2pptx-presentations`.

## Component Responsibilities

### Claude Code Manifest — `.claude-plugin/plugin.json`

Declares `name`, `version`, `description`, `author`, and `keywords`. Claude Code
auto-discovers the plugin-root `skills/` and `commands/` directories, so the
manifest stays minimal. It declares no `mcpServers` and no `hooks`. Exact
component-discovery behavior (auto-discovery vs. explicit `skills`/`commands`
fields) is verified against current Claude Code plugin docs during
implementation; the manifest is written to match whatever the installed Claude
Code version requires.

### Claude Code Marketplace — `.claude-plugin/marketplace.json`

Repo-root marketplace mirroring the Codex one:

- `name`: a marketplace identifier (e.g. `mermaid2pptx-team`);
- `owner`: minimal owner metadata;
- one `plugins[]` entry named `mermaid2pptx-presentations` with local `source`
  `./plugins/mermaid2pptx-presentations`.

Because it is repository-local, a teammate adds it explicitly
(`/plugin marketplace add .`) and then installs the plugin; the skill becomes
discoverable to Claude afterward.

### Shared Skill — `SKILL.md`

The only substantive edit: replace the hard `presentations:Presentations`
requirement with a runtime-neutral "required presentation sub-skill" section that
names the correct host skill:

- Claude Code → `anthropic-skills:pptx`
- OpenAI Codex → `presentations:Presentations`

The section instructs the agent to build narrative/ordinary slides, layout,
template fidelity, and deck-level review with the host's native presentation
skill, and to exchange complete `.pptx` files with this skill. All other workflow
content (request classification, per-diagram insertion, fast audit, full QA,
native-shape invariants, preservation of source/template decks, final-path
promotion only after success) is unchanged.

`references/presentations-integration.md` gains a short note that "the host
presentation skill" resolves to `anthropic-skills:pptx` on Claude Code and
`presentations:Presentations` on Codex. Other references are unchanged.

### Optional Slash Command — `commands/mermaid-deck.md`

A minimal `/mermaid-deck` command for Claude Code that invokes this skill and
walks the production contract (audience/goal, final path, template-or-new, per
diagram Mermaid source and target slide, acceptance criteria). It adds no new
capability; it is a convenience entry point. Codex ignores the `commands/`
directory.

### Reused Scripts and Converter

`scripts/*.ps1`, `src/Mermaid2Pptx*`, and `tests/` are unchanged. The scripts
already resolve the repository (via `-RepoRoot`, `MERMAID2PPTX_REPO`, ancestry,
and plugin-relative location), prepare .NET and Playwright, convert, insert,
audit, and publish atomically.

## Data Flow (identical on both runtimes)

```text
agent
  -> this plugin skill (mermaid2pptx-presentations)
     -> host native presentation skill        (narrative deck: base .pptx)
        - Claude Code: anthropic-skills:pptx
        - Codex:       presentations:Presentations
     -> Invoke-Mermaid2Pptx.ps1
        -> dotnet CLI: Mermaid -> native DrawingML -> --insert-into target slides
     -> Test-Mermaid2PptxDeck.ps1              (fast native-shape audit)
     -> Publish-Mermaid2PptxCandidate.ps1      (promote final .pptx only if audit passes)
```

## Invariants (reused, non-negotiable)

- Mermaid target slides contain native `p:sp` / `p:cxnSp`; diagrams stay
  editable.
- Insertion adds no `p:pic` or image relationships to target slides; standalone
  diagram decks contain no `p:pic` or `ppt/media/*`.
- Open XML validation passes.
- Source templates and source decks are preserved; the requested final path is
  written only after all insertions and audits succeed; multi-diagram runs are
  atomic at the final-output boundary.

## Validation Strategy

### Plugin structure and portability

- Claude Code manifest and marketplace parse and install; the skill is
  discoverable after install.
- Codex manifest and marketplace remain valid (no regression).
- No plugin file contains a user-specific absolute path or unsupported manifest
  field; no build/browser/QA artifacts are committed.
- Scripts still run from the repo root, a nested directory, and an outside
  directory, and honor `-RepoRoot` / `MERMAID2PPTX_REPO`.

### End-to-end demonstration

Build one real deck through the Claude Code path: create a small narrative base
deck, insert at least one Mermaid **flowchart** as native shapes, run the fast
audit, and confirm the published `.pptx` opens with editable diagram shapes and
no image parts on the diagram slide. This directly proves "an agent can use this
plugin to make a pptx."

### Known implementation risk

The converter targets `net8.0`; the local SDK is .NET 10. `dotnet run` on a
`net8.0` project may require the .NET 8 runtime or a roll-forward setting. This
is validated (and, if needed, resolved via `--framework`/roll-forward or an SDK
note) during implementation before claiming the end-to-end run succeeds.

### Converter invariants

- Existing xUnit tests pass.
- The basic CLI sample still produces a valid native-shape `.pptx`.
- Full visual/XML QA is available for high-risk diagrams but not run on every
  fast path.

## Out of Scope

- Changing Mermaid parsing, mapping, marker, or DrawingML behavior.
- Adding a local MCP server or shipping precompiled converter binaries.
- Replacing either host's native presentation skill.
- Committing generated PPTX, browser, build, or routine QA artifacts.
