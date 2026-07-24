# Presentations integration

Use `presentations:Presentations` as the owner of the full deck. It determines
the story, theme, layouts, ordinary visuals, speaker-facing structure, and
deck-level visual QA. Use Mermaid2Pptx only for slides or regions that require
editable Mermaid diagrams.

## New deck

1. Establish audience, message, slide count, final path, diagram source, diagram
   target slides, and acceptance criteria.
2. Have `presentations:Presentations` create the complete ordinary base deck at
   a unique scratch path, leaving the diagram targets ready for insertion.
3. Insert Mermaid diagrams with `Invoke-Mermaid2Pptx.ps1`.
4. Let the built-in target-slide audit pass before using each result as the next
   source.
5. Run deck-level visual review after all insertions.

For a single standalone diagram deck, skip the base-deck step and invoke the
converter directly.

## Existing template

Treat the supplied template as immutable input. Have
`presentations:Presentations` create a scratch working copy that preserves its
layout, dimensions, theme, and existing media. Inspect every diagram target on
that copy. Clear or restructure occupied target content and prepare the intended
diagram region before Mermaid insertion. Use the prepared working copy as the
first insertion source, then chain later insertions from the previous validated
scratch output.

Example path order for two diagrams:

```text
template.pptx
  -> scratch/prepared-template.pptx
  -> scratch/with-diagram-01.pptx
  -> requested-final.pptx
```

Never send `template.pptx` as `-Out`. Never use the requested final path for the
first step of a multi-diagram workflow. Never place a new diagram over occupied
slide content merely because the slide number is correct.

## Target mapping

Use 1-based `target=source` mappings. Confirm target slides against the actual
base/template deck before insertion. A fresh Mermaid code or `.mmd` conversion
produces source slide 1; an existing source PPTX may expose multiple source
slides.

Keep target slides stable between chained operations. If an upstream deck tool
adds, removes, or reorders slides, re-evaluate every mapping before invoking the
converter.

## Review responsibilities

Mermaid2Pptx validates package invariants and mapped native-shape growth.
`presentations:Presentations` validates the final narrative, layout, typography,
theme fidelity, clipping, overlap, and overall polish. Complete both when the
request is for a polished deck.
