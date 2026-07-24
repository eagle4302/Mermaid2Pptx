# Native-shape invariants

Converted Mermaid content must remain editable PowerPoint DrawingML.

## Required output

- Use native `p:sp` shapes and `p:cxnSp` connectors.
- Keep text as editable DrawingML text.
- Preserve Mermaid styling, line dashes, arrowheads, and supported markers as
  native geometry.
- Keep complex ER and class markers as separate native marker shapes.
- Exchange generated or intermediate diagrams as `.pptx` files.

## Forbidden diagram output

- No rasterized diagram screenshots.
- No PNG, JPG, SVG, or EMF diagram images.
- No `python-pptx` or PptxGenJS for the core Mermaid conversion.
- No diagram-added `ppt/media/*`, `ImagePart`, or `p:pic`.
- No raw DrawingML fragments as the integration boundary.

Existing template media and pictures may remain. In insertion mode, validate
that mapped target slides add native shapes while picture and image-relationship
counts stay equal to the source template.

## Validation levels

The fast audit in `Invoke-Mermaid2Pptx.ps1` is the default completion gate:

- standalone decks contain native shapes and no media or pictures;
- insertion outputs add native shapes on every mapped target slide;
- insertion outputs do not add pictures or image relationships;
- Mermaid syntax-error diagrams are rejected;
- the final path is published only after the checks pass.

Use full QA for visual fidelity and per-element XML comparison. QA screenshots
belong only in the QA output directory and must never be inserted into the
presentation.

When changing the converter itself, also follow the repository `AGENTS.md`, run
the xUnit suite, validate with Office 2019 Open XML rules, and inspect relevant
full-QA diffs.
