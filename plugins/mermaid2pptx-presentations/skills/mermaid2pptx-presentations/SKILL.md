---
name: mermaid2pptx-presentations
description: Use when creating or editing PowerPoint PPTX decks that need editable Mermaid diagrams, native DrawingML flowcharts, Mermaid insertion into existing templates, or Mermaid2Pptx conversion and QA.
---

# Mermaid2Pptx Presentations

## Overview

Build the deck with the host's native presentation skill; convert only Mermaid
diagrams with this skill's scripts. Exchange complete `.pptx` files between the
two workflows so every converted diagram remains editable native DrawingML.

**REQUIRED SUB-SKILL:** Use the host's native presentation skill for narrative,
ordinary slides, layout, template fidelity, and deck-level visual review:

- Claude Code: `anthropic-skills:pptx`
- OpenAI Codex: `presentations:Presentations`

The rest of this skill refers to it as "the host presentation skill".

## Production contract

Before producing files, establish:

- audience and communication goal;
- requested final `.pptx` path;
- source/template deck, or confirmation that this is a new deck;
- Mermaid source for each diagram;
- target slide for every diagram;
- acceptance criteria, including whether full visual/XML QA is required.

If the request already supplies these facts, proceed without another
confirmation. Preserve every source deck. Reserve the requested final path for
the last successful operation.

## Workflow

1. Resolve this skill's root as the directory containing this `SKILL.md`.
2. Create the ordinary base deck with the host presentation skill, writing it
   to a scratch path. For a supplied template, keep the source immutable and
   have that skill prepare the diagram targets on a scratch working copy.
   Use that prepared copy as the first insertion source.
3. Run `scripts/Invoke-Mermaid2Pptx.ps1`. It discovers or accepts the repository,
   prepares .NET and Playwright automatically, writes a candidate, performs the
   fast native-shape audit, and publishes only after validation.
4. For multiple diagrams, chain each validated result through unique scratch
   `.pptx` paths. Write the requested final path only on the last insertion.
5. Perform deck-level visual review with the host presentation skill. Run
   `scripts/Invoke-Mermaid2PptxQa.ps1` when the user requests full QA or the
   diagram is visually high-risk.

Use `-SourcePptx` when a diagram source deck already exists. Use Mermaid,
`.mmd`, stdin, or rendered HTML for fresh conversion. Never exchange raw
DrawingML fragments.

## References

- Read [CLI and QA](references/cli-and-qa.md) before invoking scripts or choosing
  source modes.
- Read [native-shape invariants](references/native-shape-invariants.md) before
  auditing output or changing conversion behavior.
- Read [Presentations integration](references/presentations-integration.md) when
  creating a full deck, using a template, or inserting multiple diagrams.

## Quick reference

| Need | Owner |
|---|---|
| Story, theme, ordinary slides, template fidelity | Host presentation skill (`anthropic-skills:pptx` / `presentations:Presentations`) |
| Editable Mermaid shapes and connectors | `Invoke-Mermaid2Pptx.ps1` |
| Default package check | Built into the invocation script |
| Full Mermaid visual/XML comparison | `Invoke-Mermaid2PptxQa.ps1` |

## Common mistakes

- Do not rasterize Mermaid diagrams or insert SVG/PNG screenshots as output.
- Do not run manual setup before the initializer; let the wrapper prepare it.
- Do not overwrite a template or reuse the final path for intermediate decks.
- Do not overlay a diagram onto occupied content; prepare its target first.
- Do not hand-roll a deck-wide ZIP check; use the target-slide baseline audit.
