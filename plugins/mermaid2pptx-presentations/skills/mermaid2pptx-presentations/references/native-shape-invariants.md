# Native-shape invariants

Converted Mermaid content must remain editable PowerPoint DrawingML.

## Required output

- Use native `p:sp` shapes and `p:cxnSp` connectors.
- Keep text as editable DrawingML text.
- Preserve Mermaid styling, line dashes, arrowheads, and supported markers as
  native geometry.
- Keep complex ER and class markers as separate native marker shapes.
- Exchange generated or intermediate diagrams as `.pptx` files.

## Text and typography acceptance

These checks are part of diagram completion, not optional polish.

### Node labels live in shape text bodies

- Single-label nodes (typical flowchart, state, and similar nodes) must carry
  their label in the parent shape's `p:txBody`.
- Those node shapes must **not** use `txBox="1"`. A separate overlay text box
  on top of the node is a defect for single-label nodes.
- Edge labels, free-floating titles, and multi-label containers (class or ER
  compartments) may remain independent text-box shapes (`txBox="1"`, no fill,
  no line).
- When reviewing slide XML, prefer shapes such as:

  ```xml
  <p:sp>
    <p:nvSpPr>...<p:cNvSpPr/></p:nvSpPr>
    <p:spPr>...geometry and fill...</p:spPr>
    <p:txBody>...node label...</p:txBody>
  </p:sp>
  ```

  Reject the single-label pattern that only places the label in a sibling
  `txBox="1"` shape.

### Theme and master fonts must be cross-platform

- Standalone Mermaid decks must not hard-code Windows-only faces such as
  `Microsoft JhengHei`, `Microsoft YaHei`, or `DengXian` in the slide master
  or theme.
- Prefer portable defaults (`Arial` for latin and east-Asian theme slots in
  Mermaid2Pptx-generated masters/themes).
- Per-run text may still request CJK fallbacks through CSS `font-family`;
  DrawingML should keep distinct `a:latin` / `a:ea` typefaces when the source
  list provides them.
- Do not treat "looks fine on Windows PowerPoint" as sufficient. Linux and
  macOS hosts must open and edit the same native shapes without relying on
  Windows-only theme fonts.

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
- standalone decks reject Windows-only master/theme fonts and keep portable
  `Arial` theme typefaces;
- standalone flowchart-style slides reject 1:1 `txBox` overlays on preset
  nodes when those nodes have no shape-owned `p:txBody` (multi-label class/ER
  compartments remain allowed);
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
