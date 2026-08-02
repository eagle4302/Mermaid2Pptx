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

Default diagram acceptance: single-label node text must live in the parent
shape `p:txBody`, and standalone master/theme fonts must stay cross-platform.
See the skill's `references/native-shape-invariants.md`.

Build the narrative and ordinary slides with the host presentation skill
(`anthropic-skills:pptx` on Claude Code), then insert every diagram as editable
native DrawingML with the skill's `scripts/Invoke-Mermaid2Pptx.ps1`. Keep any
source template immutable and write the requested final path only after every
insertion and the fast native-shape audit succeed.
