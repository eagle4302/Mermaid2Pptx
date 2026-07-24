# CLI and QA

Set `$skillRoot` to the installed directory containing this skill's `SKILL.md`.
Pass `-RepoRoot` when Codex starts outside the Mermaid2Pptx clone; otherwise the
scripts discover the repository from the current directory, the script
location, or `MERMAID2PPTX_REPO`.

```powershell
$invoke = Join-Path $skillRoot "scripts/Invoke-Mermaid2Pptx.ps1"
$qa = Join-Path $skillRoot "scripts/Invoke-Mermaid2PptxQa.ps1"
```

The invocation script automatically restores when needed, builds incrementally,
installs Playwright Chromium when needed, validates a temporary candidate, and
then publishes the destination.

## New native-shape diagram deck

```powershell
& $invoke `
  -RepoRoot "C:\team\diagram-tools\Mermaid2PPTX" `
  -Mermaid "flowchart LR; Request-->Review{Approved?}; Review-->|Yes|Done; Review-->|No|Revise; Revise-->Review" `
  -Out "C:\team\deck-work\diagram.pptx"
```

Alternative source modes:

```powershell
& $invoke -MermaidFile ".\diagram.mmd" -Out ".\diagram.pptx"
Get-Content ".\diagram.mmd" -Raw |
  & $invoke -MermaidStdin -Out ".\diagram.pptx"
& $invoke -Html ".\rendered.html" -Out ".\diagram.pptx"
```

Pass `-SlideSelector`, `-SvgSelector`, `-Width`, or `-Height` when the defaults
do not match the rendered HTML or deck.

## Insert into a deck

Convert Mermaid and insert source slide 1 into target slide 5:

```powershell
& $invoke `
  -RepoRoot "C:\team\diagram-tools\Mermaid2PPTX" `
  -MermaidFile ".\diagram.mmd" `
  -InsertInto ".\base-deck.pptx" `
  -Map "5=1" `
  -Out ".\final-deck.pptx"
```

Insert slides from an existing diagram deck:

```powershell
& $invoke `
  -SourcePptx ".\diagram-source.pptx" `
  -InsertInto ".\base-deck.pptx" `
  -Map "5=1,6=2" `
  -Out ".\final-deck.pptx"
```

Maps are 1-based `target=source` pairs. The wrapper preserves the insertion
source, refuses an existing destination unless `-Force` is explicit, and
compares mapped target slides against the original deck.

## Fast and full QA

Fast audit is mandatory and automatic in every invocation. It rejects missing
native shapes, Mermaid syntax-error slides, diagram-added pictures, and
diagram-added image relationships.

Full QA accepts rendered Mermaid HTML and produces `report.json`, reference and
converted previews, diffs, raw slide XML, and XML audit JSON:

```powershell
& $qa `
  -RepoRoot "C:\team\diagram-tools\Mermaid2PPTX" `
  -Html ".\rendered.html" `
  -Out ".\out\visual-qa"
```

Run full QA for explicit requests, converter changes, unfamiliar diagram types,
complex markers/paths, or unexplained visual differences. Inspect
`xmlAuditFailures`, `changedPixelRatio`, and the diff images. The QA output
directory may be new, empty, or an existing Mermaid2Pptx QA directory; the
wrapper refuses to clean unrelated non-empty directories.

Failed conversion candidates are retained under the repository's
`out/plugin-diagnostics/` directory. Do not publish those candidates.
