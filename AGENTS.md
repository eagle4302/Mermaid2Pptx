# AGENTS.md

This file is for AI coding agents working on Mermaid2Pptx. Read it before
changing code. The project goal is to convert rendered Mermaid SVG and draw.io
XML into an editable PowerPoint deck made from native DrawingML shapes.

## Core Invariants

- Generated `.pptx` files must use native DrawingML shapes and text.
- Do not rasterize diagrams, take screenshots for output, embed PNG/JPG, insert
  SVG as an image, use EMF, Inkscape, `python-pptx`, or PptxGenJS for the core
  converter.
- The output package must not contain `ppt/media/*`, `ImagePart`, or `p:pic`
  entries for converted diagrams.
- Visual QA screenshots are allowed only as QA artifacts under `out/`; they must
  never become part of generated PPTX files.
- Keep generated artifacts out of source changes unless the task explicitly asks
  for them. Avoid editing `bin/`, `obj/`, `.vs/`, and routine `out/` artifacts.

## Solution Map

- `src/Mermaid2Pptx/` contains the core CLI and conversion library.
- `src/Mermaid2Pptx.Web/` contains the minimal Web UI that accepts Mermaid code
  or draw.io XML and downloads a generated native-shape PPTX.
- `src/Mermaid2Pptx.Qa/` contains visual and XML QA tooling.
- `tests/Mermaid2Pptx.Tests/` contains xUnit tests for native-shape conversion,
  DrawingML validity, style handling, marker behavior, and XML audit checks.
- `samples/` contains rendered Mermaid HTML inputs and a draw.io flowchart used
  for manual and QA runs.
- `tools/SmartFactoryDeck/` is a separate deck-generation tool; do not change it
  while working on core conversion unless the request is specifically about that
  tool.

## Conversion Pipeline

The high-level flow is:

```text
Mermaid HTML or draw.io XML
  -> MermaidSvgExtractor or DrawIoDocumentParser
  -> SvgScene
  -> SvgToPowerPointMapper
  -> DrawingMlWriter
  -> .pptx package with native DrawingML
```

Main entry points:

- `Program.cs` parses CLI options and calls `MermaidPptxConverter`.
- `MermaidPptxConverter.ConvertHtmlFileAsync(...)` orchestrates the full HTML to
  PPTX pipeline.
- `MermaidPptxConverter.ConvertMermaidCodeAsync(...)` builds a temporary HTML
  page that loads Mermaid from jsDelivr, then uses the same HTML pipeline.
- `MermaidPptxConverter.ConvertDrawIoXml(...)` / `ConvertDrawIoFileAsync(...)`
  parse draw.io `mxGraphModel` cells into `SvgScene`, then use the same mapper
  and DrawingML writer.
- `Mermaid2Pptx.Web/Program.cs` posts Mermaid code or draw.io XML to the
  converter and returns the PPTX download.

## CLI Usage For Agents

AI agents should pass Mermaid or draw.io source to the CLI and exchange `.pptx`
files, not raw DrawingML fragments. Generated decks remain native DrawingML
shape decks.

Standalone diagram input:

```powershell
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --mermaid "graph TD; A-->B" --out out/diagram.pptx
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --mermaid-file diagram.mmd --out out/diagram.pptx
Get-Content diagram.mmd -Raw | dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --mermaid-stdin --out out/diagram.pptx
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --drawio-file samples/flowchart.drawio --out out/flowchart.pptx
```

Insert Mermaid-generated native shapes into an existing deck:

```powershell
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --mermaid-file diagram.mmd --insert-into base.pptx --map "5=1" --out final.pptx
```

Source selection rules:

- Standalone conversion accepts exactly one source: `--html`, `--mermaid`,
  `--mermaid-file`, `--mermaid-stdin`, `--drawio`, `--drawio-file`, or
  `--drawio-stdin`.
- Insert mode accepts either `--source-pptx` or exactly one conversion source.
- `--map` uses 1-based `target=source` slide pairs, for example `"5=1,6=2"`.
- Mermaid code input creates one source slide in v1.
- Draw.io input creates one source slide per `<diagram>` page.

### 1. SVG Extraction

`MermaidSvgExtractor` uses Playwright to open the input HTML, wait for Mermaid to
finish, and collect outer SVG markup.

Important details:

- Default selectors are `.slide` for slides and `svg` for SVGs.
- If no slide selector matches, extraction falls back to `body`.
- Nested SVGs are ignored unless they are the outer SVG for a diagram.
- Extracted bounding boxes are stored in `ExtractedSvg` so the mapper can place a
  diagram inside its original region on the slide.
- If bundled Playwright Chromium is missing, the extractor falls back to local
  `msedge` and then `chrome`.

### 1a. Draw.io Parsing

`DrawIoDocumentParser` reads diagrams.net `mxfile` / `mxGraphModel` XML and
builds `SvgScene` objects without Playwright.

Important details:

- Uncompressed pages and compressed (Base64 + deflate) `<diagram>` payloads are
  both supported.
- Each `<diagram>` page becomes one `SvgScene` / PPTX slide.
- Vertices map to PowerPoint preset geometry when a match exists (`rect`,
  `roundRect`, `ellipse`, `diamond`, flowchart stencils). Unknown stencils fall
  back to a rectangle and add a warning.
- Edges with two points become native connectors (`p:cxnSp`); routed edges with
  waypoints become custom-geometry polylines.
- Simple `endArrow` / `startArrow` values become native PowerPoint arrowheads.
- HTML labels are flattened to plain text, including `<br>` line breaks.

### 2. SVG Parsing

`SvgDocumentParser` parses browser SVG markup into `SvgScene`.

The parser currently handles:

- Containers: `g`, `a`, `switch`, nested `svg`.
- Shapes: `rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`, `path`.
- Text: `text`, `tspan`, and extractable text from `foreignObject`.
- Definitions: markers from `defs`.
- Styles: presentation attributes, inline style, and scoped CSS rules via
  `SvgStyleResolver`.
- Transforms: matrix composition via `SvgTransformResolver`.

Unsupported visible elements should add a warning and still attempt child
conversion when possible. Hidden elements should not produce shapes.

`SvgPathParser` normalizes path data into internal segments:

- `M/L/H/V/C/S/Q/T/A/Z` commands are supported.
- Relative commands are converted to absolute coordinates.
- Smooth curves are expanded with reflected control points.
- Arcs are approximated as cubic Bezier segments.

### 3. Mapping To PPTX Model

`SvgToPowerPointMapper` maps `SvgScene` to `PptxSlideModel`.

Important model choices:

- Axis-aligned `rect` maps to preset `rect` or `roundRect`.
- `circle` and `ellipse` map to preset `ellipse`.
- `line` maps to `PptxShapeKind.Line`, which writes as a native connector
  (`p:cxnSp`).
- `polyline`, `polygon`, and complex paths map to custom geometry.
- Text: single node labels merge into the parent shape's DrawingML text body;
  edge labels and multi-label containers stay as separate text-box shapes with
  no fill and no line.
- Region mapping uses `ExtractedSvg` layout data when available; otherwise it
  maps the SVG viewBox into the slide viewport with a margin.

Marker behavior is intentionally specific:

- Simple triangle markers become native PowerPoint line arrowheads.
- `marker-start` maps to `ArrowStart`; `marker-end` maps to `ArrowEnd`.
- `DrawingMlWriter` currently emits `ArrowStart` as `a:headEnd` and `ArrowEnd`
  as `a:tailEnd`; tests depend on this orientation, especially for flipped
  connectors.
- Complex Mermaid markers, including ER cardinality markers and class extension
  markers, must remain separate native marker shapes named with the `Marker `
  prefix. Do not collapse them into triangle arrowheads.

### 4. Writing DrawingML

`DrawingMlWriter` creates the PPTX package directly with Open XML SDK parts and
hand-authored XML strings.

It is responsible for:

- Presentation, slide master, blank layout, theme, slide parts, and relationships.
- Shape XML (`p:sp`) and connector XML (`p:cxnSp`).
- Preset geometry, custom geometry, fill, stroke, dash, arrowhead, and text body
  XML.
- XML escaping for shape names, text, and font names.
- Normalized `[Content_Types].xml`.

Keep new XML valid for `OpenXmlValidator(FileFormatVersions.Office2019)`.

## QA And Tests

Use these commands from the repository root.

Restore and build:

```powershell
dotnet restore
dotnet build Mermaid2Pptx.sln
```

Install Playwright Chromium after the core project has been built:

```powershell
pwsh src/Mermaid2Pptx/bin/Debug/net8.0/playwright.ps1 install chromium
```

Run unit tests:

```powershell
dotnet test tests/Mermaid2Pptx.Tests/Mermaid2Pptx.Tests.csproj
```

Run the CLI on the basic sample:

```powershell
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --html samples/sample.html --out out/sample.pptx --slide-selector ".slide" --svg-selector "svg" --width 13.333 --height 7.5
```

Run the multi-diagram sample:

```powershell
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --html samples/all-diagrams.html --out out/all-diagrams.pptx --slide-selector ".slide" --svg-selector "svg"
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --drawio-file samples/flowchart.drawio --out out/flowchart.pptx
```

Run visual and XML QA:

```powershell
dotnet run --project src/Mermaid2Pptx.Qa/Mermaid2Pptx.Qa.csproj -- --html samples/all-diagrams.html --out out/visual-qa-all --slide-selector ".slide" --svg-selector "svg"
```

The QA command writes reference PNG/SVG files, converted preview SVG/HTML files,
diff PNG files, raw PPTX slide XML, per-item XML audit JSON, and `report.json`.
Use `xmlAuditFailures`, `changedPixelRatio`, and the diff images to decide
whether a mapping change is acceptable.

## How To Add Or Fix Conversion Support

1. Start with a minimal SVG, Mermaid, or draw.io sample that reproduces the issue.
2. If an SVG primitive is missing, add or extend an `SvgElement` model in
   `Models.cs`.
3. Parse the primitive in `SvgDocumentParser` or `DrawIoDocumentParser`,
   preserving style and transform.
4. If a new style property is needed, update `SvgStyle`, `SvgStyleResolver`, the
   mapper, and `DrawingMlWriter` together.
5. If a new path or transform form is needed, update `SvgPathParser` or
   `SvgTransformResolver` and add focused tests.
6. Map the parsed element in `SvgToPowerPointMapper`. Prefer preset PowerPoint
   geometry when it is semantically correct; use custom geometry for complex
   paths. New draw.io stencils belong in `DrawIoDocumentParser.ResolvePreset`.
7. Update `DrawingMlWriter` only when the existing PPTX model cannot express the
   needed DrawingML.
8. Add or extend xUnit tests to assert native shape XML, no media parts, and any
   important style/marker behavior.
9. Run unit tests. For visual behavior, run QA against `samples/all-diagrams.html`
   and inspect `out/visual-qa-all/report.json` plus the diff images.

## Test Expectations To Preserve

The tests assert several non-negotiable behaviors:

- Generated decks contain native shape XML.
- Generated decks contain no `ppt/media/*` entries.
- Generated slide XML contains no `p:pic`.
- Open XML validation passes.
- Colors from scoped Mermaid CSS are preserved in DrawingML.
- Dashed SVG lines become native dashed DrawingML lines.
- SVG lines become native connectors (`p:cxnSp`).
- Draw.io vertices become native preset shapes and two-point edges become
  connectors.
- Simple marker-end arrows are emitted as native PowerPoint arrowheads.
- ER and class extension markers remain native marker shapes, not simple
  arrowheads.
- XML audit can map source SVG elements to generated slide XML and validate
  connector endpoints, dash, marker ends, and flipped arrows.

## Debugging Notes

- If extraction fails, check selectors first, then Mermaid render timing.
- If Playwright cannot launch, build the core project and install Chromium using
  the generated `playwright.ps1`; local Edge/Chrome can also be used as fallback.
- If visual QA shows differences but XML audit passes, inspect the converted
  preview SVG and the raw `*-pptx-slide.xml` before changing writer behavior.
- If PowerPoint opens the deck but tests fail, trust the package tests: PowerPoint
  may repair or tolerate invalid XML that should not be emitted.
- When changing line or marker orientation, run the marker and connector tests;
  this area has PowerPoint-specific direction handling.
