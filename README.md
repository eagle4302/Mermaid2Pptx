# Mermaid2Pptx

MVP converter for Mermaid-rendered HTML SVG to editable PowerPoint DrawingML. The converter uses Playwright to read rendered SVG from an HTML file, parses SVG DOM with `System.Xml.Linq`, maps visible SVG primitives to native DrawingML shapes/text/custom geometry, and writes `.pptx` with Open XML SDK.

It does not rasterize, screenshot, insert PNG/JPG, insert SVG as an image, use EMF, Inkscape, `python-pptx`, or PptxGenJS.

## Project Structure

```text
src/Mermaid2Pptx/
  Program.cs
  MermaidSvgExtractor.cs
  SvgDocumentParser.cs
  SvgStyleResolver.cs
  SvgTransformResolver.cs
  SvgPathParser.cs
  SvgToPowerPointMapper.cs
  DrawingMlWriter.cs
  UnitConversion.cs
  Models.cs
  PptxModel.cs
tests/Mermaid2Pptx.Tests/
  ConversionTests.cs
samples/
  sample.html
  all-diagrams.html
```

## Repository Artifact Policy

Commit source code, tests, plugin files, documentation, and reproducible
Mermaid, HTML, or Markdown inputs.

Do not commit generated PowerPoint decks, QA output, build artifacts, or local
presentation projects. Generated `.pptx` files are ignored globally, and local
deliverables belong under `outputs/`. If a binary PPTX fixture is ever required,
add a narrowly scoped `.gitignore` exception together with the test that needs
it.

## Run

```powershell
dotnet restore
dotnet build src/Mermaid2Pptx/Mermaid2Pptx.csproj
pwsh src/Mermaid2Pptx/bin/Debug/net8.0/playwright.ps1 install chromium
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --html samples/sample.html --out out/sample.pptx --slide-selector ".slide" --svg-selector "svg" --width 13.333 --height 7.5
```

You can also pass Mermaid source directly and still get a native-shape PPTX:

```powershell
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --mermaid "graph TD; A-->B" --out out/inline-mermaid.pptx
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --mermaid-file diagram.mmd --out out/diagram.pptx
Get-Content diagram.mmd -Raw | dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --mermaid-stdin --out out/diagram.pptx
```

For AI agents, the recommended exchange format is still `.pptx`, not raw
DrawingML fragments. Generate a temporary native-shape diagram deck, or insert it
directly into an existing deck:

```powershell
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --mermaid-file diagram.mmd --insert-into base.pptx --map "5=1" --out final.pptx
```

CLI source inputs are mutually exclusive: use exactly one of `--html`,
`--mermaid`, `--mermaid-file`, or `--mermaid-stdin`, unless insert mode uses an
existing native-shape source deck via `--source-pptx`.

Seven-diagram verification sample:

```powershell
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --html samples/all-diagrams.html --out out/all-diagrams.pptx --slide-selector ".slide" --svg-selector "svg"
```

For minimal headless installs, Playwright also supports:

```powershell
pwsh src/Mermaid2Pptx/bin/Debug/net8.0/playwright.ps1 install --only-shell chromium
```

## Run on macOS

Install the .NET 8 SDK first. If `pwsh` is not available, install PowerShell for
macOS before installing Playwright browsers.

```bash
dotnet restore
dotnet build src/Mermaid2Pptx/Mermaid2Pptx.csproj
pwsh src/Mermaid2Pptx/bin/Debug/net8.0/playwright.ps1 install chromium
dotnet run --project src/Mermaid2Pptx/Mermaid2Pptx.csproj -- --html samples/sample.html --out out/sample.pptx --slide-selector ".slide" --svg-selector "svg" --width 13.333 --height 7.5
```

For a minimal headless browser install on macOS:

```bash
pwsh src/Mermaid2Pptx/bin/Debug/net8.0/playwright.ps1 install --only-shell chromium
```

Run the Web UI on macOS with:

```bash
dotnet run --project src/Mermaid2Pptx.Web/Mermaid2Pptx.Web.csproj --urls http://127.0.0.1:5088
```

Then open `http://127.0.0.1:5088` in a browser. The `Start-WebUI.bat` and
`Stop-WebUI.bat` helper scripts are Windows-only.

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

## Test

```powershell
dotnet test tests/Mermaid2Pptx.Tests/Mermaid2Pptx.Tests.csproj
```

The tests verify that generated PPTX files contain native shape XML and no `ppt/media/*` raster image parts.

If bundled Playwright Chromium is missing, the CLI automatically falls back to local `msedge` and then `chrome` channels.

## Web UI

On Windows, double-click `Start-WebUI.bat`, or run manually:

```powershell
dotnet run --project src/Mermaid2Pptx.Web/Mermaid2Pptx.Web.csproj --urls http://127.0.0.1:5088
```

Open `http://127.0.0.1:5088`, paste Mermaid code, and download the generated native-shape PPTX.

## Visual QA

```powershell
dotnet run --project src/Mermaid2Pptx.Qa/Mermaid2Pptx.Qa.csproj -- --html samples/sample.html --out out/visual-qa --slide-selector ".slide" --svg-selector "svg"
```

For the seven supported Mermaid families:

```powershell
dotnet run --project src/Mermaid2Pptx.Qa/Mermaid2Pptx.Qa.csproj -- --html samples/all-diagrams.html --out out/visual-qa-all --slide-selector ".slide" --svg-selector "svg"
```

The QA command writes one `*-reference.png`, `*-converted.png`, `*-diff.png`, `*-converted-preview.svg`, `*-pptx-slide.xml`, and `*-xml-audit.json` per diagram, plus an aggregate `report.json`. The XML audit maps parsed SVG elements to the generated PPTX slide XML and checks native shape kind, connector mapping, dash, marker endpoints, and flipped connector arrowheads. Screenshots are QA artifacts only; they are never embedded into the generated PPTX.

Current verification sample covers Flowchart, Class, Sequence, Entity Relationship, State, Mindmap, and Architecture diagrams. The mapper keeps them as native DrawingML shapes/text/custom geometry, including native connector arrowheads, dashed connector lines, complex ER/state marker geometry, and nested SVG icon geometry.
