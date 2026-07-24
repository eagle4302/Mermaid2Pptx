# Mermaid2Pptx Presentations Plugin Design

Date: 2026-07-24

## Goal

Create a repository-local, team-portable Codex plugin that lets an agent build
complete PowerPoint presentations and use Mermaid2Pptx whenever a workflow,
architecture, sequence, or other Mermaid diagram is needed. Mermaid diagrams
must remain editable native DrawingML shapes after they are inserted into the
deck.

The plugin must support:

- building a new presentation from scratch;
- applying or modifying an existing PPTX template;
- inserting one or more Mermaid diagrams into either workflow;
- automatic first-use preparation of the Mermaid2Pptx environment;
- fast validation after normal runs and optional full visual/XML QA.

## Current Structure

The repository already has the correct ownership boundaries:

- `src/Mermaid2Pptx/` owns Mermaid rendering, SVG parsing, PowerPoint mapping,
  native DrawingML generation, and insertion into existing decks.
- `src/Mermaid2Pptx.Qa/` owns visual and XML QA.
- `tests/Mermaid2Pptx.Tests/` owns converter and package invariants.
- The installed `Presentations` skill owns presentation narrative, layout,
  template following, Artifact Tool authoring, rendering, and presentation
  polish.
- The existing personal `mermaid2pptx-presentations` skill contains useful
  workflow guidance and wrappers, but its wrappers contain a fixed
  `E:\project\Mermaid2PPTX` fallback and do not provide automatic bootstrap or
  target-slide package validation.

The plugin will reuse the mature workflow guidance while removing machine-local
assumptions. It will not move or duplicate converter business logic.

## Target Placement

The repository will contain:

```text
plugins/
  mermaid2pptx-presentations/
    .codex-plugin/
      plugin.json
    skills/
      mermaid2pptx-presentations/
        SKILL.md
        agents/
          openai.yaml
        references/
          cli-and-qa.md
          native-shape-invariants.md
          presentations-integration.md
        scripts/
          Initialize-Mermaid2Pptx.ps1
          Invoke-Mermaid2Pptx.ps1
          Test-Mermaid2PptxDeck.ps1
          Invoke-Mermaid2PptxQa.ps1
.agents/
  plugins/
    marketplace.json
```

The normalized plugin name, plugin directory, manifest name, marketplace entry,
and user-facing deep links will all use `mermaid2pptx-presentations`.

No compiled converter binaries, Playwright browser files, generated decks, or
routine QA artifacts will be committed to the plugin.

## Architecture Decision

Use a skill-first plugin with PowerShell command adapters.

The plugin is an application-orchestration layer:

1. Route full-deck strategy and ordinary slide production to `Presentations`.
2. Reserve or select the target slides that will contain Mermaid diagrams.
3. Prepare Mermaid2Pptx and convert Mermaid sources through the existing C# CLI.
4. Insert the generated native shapes into the base deck.
5. Compare the target Mermaid slides with the pre-insertion base deck using the
   fast package audit.
6. Escalate to the repository QA project for high-risk or explicitly requested
   visual/XML validation.

The dependency direction is:

```text
Codex agent
  -> repository plugin skill
     -> Presentations skill for deck production
     -> plugin PowerShell adapters
        -> Mermaid2Pptx CLI and QA projects
```

The C# converter and QA projects do not depend on the plugin. The plugin does
not reimplement Mermaid parsing, DrawingML writing, slide design, or template
editing.

### Rejected Alternatives

#### Local MCP Server

A local MCP server would give the agent structured tool calls, but it would add
server lifecycle, protocol, configuration, and cross-platform failure modes
without improving the existing CLI conversion model enough to justify the
cost.

#### Bundled Published Converter

Bundling `dotnet publish` output would reduce first-use build time, but it would
add generated binaries, require per-platform packages, increase repository
size, and create version drift between the plugin and converter source.

## Component Responsibilities

### Plugin Manifest

`.codex-plugin/plugin.json` declares the normalized name, semantic version,
description, author, skill path, and interface metadata. It will not declare
apps or MCP servers because the plugin does not create those components.

### Marketplace Entry

`.agents/plugins/marketplace.json` exposes the repository-local plugin with:

- local source `./plugins/mermaid2pptx-presentations`;
- installation policy `AVAILABLE`;
- authentication policy `ON_INSTALL`;
- category `Productivity`.

The marketplace must be installed explicitly because it is repository-local.

### Workflow Skill

`SKILL.md` is the agent-facing orchestration contract. It will:

- classify requests as new deck, template-based deck, targeted edit,
  standalone diagram, converter development, or QA/debugging;
- require `Presentations` for narrative, layout, templates, and general slides;
- require Mermaid2Pptx for editable Mermaid diagrams;
- enforce the native-shape invariants;
- define the fast-validation and full-QA routes;
- route converter code changes through the repository and applicable
  Superpowers workflows;
- use project-relative scripts rather than embedding fragile command sequences.

### Environment Initialization

`Initialize-Mermaid2Pptx.ps1` owns infrastructure preparation:

- resolve the repository from `-RepoRoot`, `MERMAID2PPTX_REPO`, the current
  directory ancestry, and the plugin's repository-relative location;
- reject a candidate unless it contains `Mermaid2Pptx.sln` and the expected
  core project;
- detect a compatible .NET 8 SDK;
- run restore and build when required;
- install Playwright Chromium when required;
- remain safe and repeatable on subsequent runs;
- return actionable failure details without embedding a user-specific path.

Repository discovery and environment preparation remain domain-specific
PowerShell functions in this script. They will not be placed in a vague
utilities module.

### Conversion And Insertion

`Invoke-Mermaid2Pptx.ps1` owns CLI orchestration:

- accept inline Mermaid, `.mmd`, stdin, rendered HTML, or an existing
  native-shape source deck using the same source-selection rules as the C# CLI;
- invoke environment initialization before conversion;
- forward supported converter options without changing converter semantics;
- support standalone diagram decks and insertion into a base PPTX;
- use temporary candidate files and publish only after successful validation;
- refuse to overwrite an existing destination unless `-Force` is explicit;
- preserve the original template or source deck.

For multiple Mermaid diagrams, the workflow converts and inserts each source
against a temporary successor deck. The final destination is promoted only
after every insertion and validation succeeds.

### Fast Deck Audit

`Test-Mermaid2PptxDeck.ps1` performs the default post-run check. Given a
candidate PPTX, one or more 1-based Mermaid target slide numbers, and the
pre-insertion base deck when insertion mode is used, it will:

- resolve the ordered slide parts through presentation relationships;
- confirm native shape or connector counts increase on each insertion target;
- confirm insertion adds no `p:pic` elements or image relationships compared
  with the corresponding slide in the base deck;
- require standalone Mermaid diagram decks to contain no `p:pic` or
  `ppt/media/*` entries;
- confirm the package can be opened as a ZIP and required presentation parts
  exist;
- report results per target slide.

The comparison is scoped to Mermaid target slides. Pre-existing images on those
slides and images elsewhere in a complete presentation, such as photos and
logos, remain valid.

### Full QA

`Invoke-Mermaid2PptxQa.ps1` invokes `src/Mermaid2Pptx.Qa` for explicit full QA,
converter behavior changes, complex marker/connector/style risk, or follow-up
after a fast audit failure. Generated reports remain under `out/` or another
user-selected scratch directory.

## Presentation Workflows

### New Presentation

1. Use `Presentations` to establish audience, narrative, visual route, slide
   plan, and base deck.
2. Reserve blank or purpose-built target slides for Mermaid diagrams.
3. Save each diagram source as a temporary `.mmd` when needed.
4. Bootstrap Mermaid2Pptx.
5. Insert native diagram shapes using explicit 1-based slide mappings.
6. Run the fast audit on the Mermaid target slides.
7. Render and inspect the finished presentation through the `Presentations`
   quality workflow.
8. Publish the final PPTX only after checks pass.

### Existing Template Or Deck

1. Use `Presentations` template-following mode to inspect every source slide,
   master, layout, and inherited element.
2. Create a copy and perform general edits in that copy.
3. Reserve target slides while preserving the template's visual system.
4. Insert Mermaid native shapes only into the mapped slides.
5. Keep the original PPTX unchanged.
6. Run target-slide native-shape validation and template fidelity checks before
   delivery.

## Failure Handling

- Missing .NET 8 stops before restore or conversion and lists the detected SDKs.
- Restore, build, or Chromium installation failure reports the failed command
  and a concise actionable error.
- Invalid or empty Mermaid input identifies the source and target slide.
- A target slide outside the base deck range is rejected before conversion.
- Existing output is not overwritten without `-Force`.
- A conversion warning remains visible to the agent.
- A non-zero converter or QA exit code stops the workflow.
- A fast-audit failure prevents publication of the final output and preserves
  the candidate under a diagnostic scratch location.
- Multi-diagram operations are atomic at the final-output boundary; partial
  intermediate decks never replace the requested destination.

## Validation Strategy

### Structure

- Run the plugin creator validator on the plugin root.
- Run the skill validator on the skill folder.
- Validate the repository marketplace entry.
- Scan plugin files for fixed machine paths, placeholders, unsupported manifest
  fields, and bundled build artifacts.

### Portability

- Invoke scripts from the repository root, a nested repository directory, and
  a directory outside the repository.
- Verify explicit `-RepoRoot` and `MERMAID2PPTX_REPO` selection.
- Copy the repository to a temporary path and verify discovery without the
  original path.

### Bootstrap

- Run from a state without usable build output and confirm restore/build and
  browser preparation.
- Run again to confirm the workflow is idempotent.
- Exercise the missing or incompatible SDK diagnostic path.

### End-To-End Presentation Scenarios

- Build a new presentation and insert at least one Mermaid flowchart.
- Copy an existing PPTX template and insert a Mermaid diagram without modifying
  the source file.
- Insert multiple Mermaid diagrams into one deck.
- Verify bad Mermaid, invalid mappings, and existing output paths fail safely.

### Package And Converter Invariants

- Mermaid target slides contain native `p:sp` or `p:cxnSp` elements.
- Insertion adds no `p:pic` elements or image relationships to Mermaid target
  slides, while standalone Mermaid decks contain no `p:pic` or media parts.
- Open XML validation passes.
- Legitimate non-diagram media elsewhere in a deck is not treated as a failure.
- Existing xUnit tests pass.
- The basic CLI sample produces a valid native-shape PPTX.
- Full visual/XML QA is run for high-risk changes, not on every fast path.

### Agent Behavior

Follow skill TDD:

1. Run realistic new-deck and template-deck scenarios without the new plugin
   skill and record the baseline failure or omission.
2. Implement the minimal skill guidance needed to correct the observed
   behavior.
3. Run equivalent scenarios with the plugin skill.
4. Verify the agent uses `Presentations` for the base deck, Mermaid2Pptx for
   diagrams, explicit slide mappings, and target-slide validation.
5. Refine the skill only for failures demonstrated by the forward tests.

## Acceptance Criteria

The plugin is complete when:

- a teammate can clone the repository, install its local marketplace and
  plugin, and start a new Codex task that discovers the skill;
- no plugin script contains a user-specific absolute path;
- first use prepares the converter environment automatically;
- new and template-based PPTX workflows both succeed;
- one or more Mermaid diagrams can be inserted through explicit mappings;
- Mermaid diagrams remain editable native DrawingML shapes;
- normal runs perform fast target-slide validation;
- high-risk runs can invoke full visual/XML QA;
- source templates are preserved and incomplete final outputs are not
  published;
- plugin, skill, marketplace, converter tests, and representative end-to-end
  checks pass.

## Out Of Scope

- Replacing the `Presentations` skill or Artifact Tool.
- Adding a new local MCP server.
- Shipping precompiled converter binaries.
- Changing Mermaid2Pptx parsing, mapping, marker, or DrawingML behavior.
- Supporting Google Slides as a direct native editing target beyond the routing
  already provided by `Presentations`.
- Committing generated PPTX, browser, build, or routine QA artifacts.
