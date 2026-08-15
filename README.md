# Mermaid2Pptx

MVP converter for Mermaid-rendered HTML SVG and draw.io / diagrams.net XML to
editable PowerPoint DrawingML. Mermaid input uses Playwright to read rendered
SVG from an HTML file. Draw.io input parses `mxGraphModel` cells directly. Both
paths map visible primitives to native DrawingML shapes/text/custom geometry and
write `.pptx` with Open XML SDK.

It does not rasterize, screenshot, insert PNG/JPG, insert SVG as an image, use EMF, Inkscape, `python-pptx`, or PptxGenJS.

## Project Structure

```text
src/Mermaid2Pptx/
  Program.cs
  MermaidSvgExtractor.cs
  SvgDocumentParser.cs
  DrawIoDocumentParser.cs
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
  flowchart.drawio
```

## Repository Artifact Policy

Commit source code, tests, plugin files, documentation, and reproducible
Mermaid, HTML, or Markdown inputs.

Do not commit generated PowerPoint decks, QA output, build artifacts, or local
presentation projects. Generated `.pptx` files are ignored globally, and local
deliverables belong under `outputs/`. If a binary PPTX fixture is ever required,
add a narrowly scoped `.gitignore` exception together with the test that needs
it.

## Install The CLI

Install the .NET 8 SDK, then pack and install the repository CLI:

```powershell
dotnet restore
dotnet pack src/Mermaid2Pptx/Mermaid2Pptx.csproj --configuration Release --output out/packages
dotnet tool install --global Mermaid2Pptx.Tool --add-source (Resolve-Path out/packages) --version 0.1.0
mermaid2pptx setup
```

`setup` installs Playwright Chromium once. The converter can fall back to local
Edge or Chrome, but the bundled Chromium produces the most predictable result.

Use `dotnet tool update` with the same source and version options when
installing a newer package version.

## Run

Convert rendered HTML or Mermaid source directly:

```powershell
mermaid2pptx --html samples/sample.html --out out/sample.pptx --slide-selector ".slide" --svg-selector "svg" --width 13.333 --height 7.5
mermaid2pptx --mermaid "graph TD; A-->B" --out out/inline-mermaid.pptx
mermaid2pptx --mermaid-file diagram.mmd --out out/diagram.pptx
Get-Content diagram.mmd -Raw | mermaid2pptx --mermaid-stdin --out out/diagram.pptx
mermaid2pptx --drawio-file samples/flowchart.drawio --out out/flowchart.pptx
Get-Content samples/flowchart.drawio -Raw | mermaid2pptx --drawio-stdin --out out/flowchart.pptx
mermaid2pptx --mermaid-file diagram.mmd --out out/diagram.drawio
mermaid2pptx --drawio-file samples/flowchart.drawio --out out/flowchart.mmd
```

For AI agents, the recommended exchange format is still `.pptx`, not raw
DrawingML fragments. Generate a temporary native-shape diagram deck, or insert it
directly into an existing deck:

```powershell
mermaid2pptx --mermaid-file diagram.mmd --insert-into base.pptx --map "5=1" --out final.pptx
```

CLI source inputs are mutually exclusive: use exactly one of `--html`,
`--mermaid`, `--mermaid-file`, `--mermaid-stdin`, `--drawio`, `--drawio-file`,
or `--drawio-stdin`, unless insert mode uses an existing native-shape source
deck via `--source-pptx`. Draw.io files may be uncompressed `mxfile` XML or
compressed diagrams.net pages; each `<diagram>` page becomes one PPTX slide.

Flowchart interop (no Playwright): `--out` extension or `--to` selects the
format. Mermaid `flowchart`/`graph` converts to draw.io XML and back. Sequence,
class, ER, and other Mermaid diagram types still convert to PPTX only.

Seven-diagram verification sample:

```powershell
mermaid2pptx --html samples/all-diagrams.html --out out/all-diagrams.pptx --slide-selector ".slide" --svg-selector "svg"
```

For repository-local development without a global tool installation:

```powershell
dotnet build Mermaid2Pptx.sln
.\src\Mermaid2Pptx\bin\Debug\net8.0\Mermaid2Pptx.exe setup
.\src\Mermaid2Pptx\bin\Debug\net8.0\Mermaid2Pptx.exe --mermaid "graph TD; A-->B" --out out/diagram.pptx
```

## Run on macOS

Install the .NET 8 SDK first. The installed CLI does not require PowerShell:

```bash
dotnet restore
dotnet pack src/Mermaid2Pptx/Mermaid2Pptx.csproj --configuration Release --output out/packages
dotnet tool install --global Mermaid2Pptx.Tool --add-source "$(pwd)/out/packages" --version 0.1.0
mermaid2pptx setup
mermaid2pptx --html samples/sample.html --out out/sample.pptx --slide-selector ".slide" --svg-selector "svg" --width 13.333 --height 7.5
```

Run the Web UI on macOS or Linux with the helper script:

```bash
./start-webui.sh
```

It checks for the .NET SDK, starts the server, and opens your browser at
`http://127.0.0.1:5088`. Stop it with `Ctrl+C`, or from another terminal:

```bash
./stop-webui.sh
```

You can also start the server manually without the helper:

```bash
dotnet run --project src/Mermaid2Pptx.Web/Mermaid2Pptx.Web.csproj --urls http://127.0.0.1:5088
```

`start-webui.sh` / `stop-webui.sh` are the macOS/Linux counterparts of the
Windows `Start-WebUI.bat` / `Stop-WebUI.bat` helpers.

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

On Windows, double-click `Start-WebUI.bat` (stop with `Stop-WebUI.bat`). On macOS
or Linux, run `./start-webui.sh` (stop with `./stop-webui.sh`). Either way, you
can also start the server manually:

```powershell
dotnet run --project src/Mermaid2Pptx.Web/Mermaid2Pptx.Web.csproj --urls http://127.0.0.1:5088
```

Open `http://127.0.0.1:5088`, paste Mermaid code or draw.io XML, and download PPTX, draw.io, or Mermaid.

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
