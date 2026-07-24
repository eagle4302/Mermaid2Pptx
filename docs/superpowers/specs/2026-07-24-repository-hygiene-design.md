# Repository Hygiene Design

**Date:** 2026-07-24
**Status:** Approved
**Scope:** Documentation, generated PowerPoint artifacts, ignore rules, and the unpushed Git history

## Goal

Keep the Mermaid2Pptx repository focused on source code, tests, plugin assets,
and reproducible inputs. Preserve private or project-specific deliverables
locally without publishing them, and remove generated PowerPoint files that can
be rebuilt from source.

## Current Structure Summary

- `master` is eleven commits ahead of `origin/master`.
- Nine unpushed commits implement the portable
  `mermaid2pptx-presentations` plugin.
- Two earlier unpushed commits contain ZYNQ-specific presentation design and
  implementation documents that do not belong to the converter or plugin.
- Six generated `.pptx` files are tracked in the repository.
- `out/` and build output directories are already ignored.
- `outputs/` contains local presentation projects and deliverables but is not
  currently ignored.
- `.worktrees/zynq-validation-deck/` is an active linked worktree and must not
  be modified by this cleanup.

## Repository Content Policy

### Commit And Push

- Converter, Web UI, QA, and plugin source code.
- Automated tests and test scripts.
- Mermaid, HTML, Markdown, and other text inputs that reproduce examples.
- Repository documentation that explains Mermaid2Pptx or its plugin.
- Plugin manifests, skill instructions, references, and marketplace metadata.

### Keep Local And Ignore

- `outputs/` and everything below it.
- Generated `.pptx` files regardless of directory.
- `out/`, QA renders, diagnostics, previews, and other reproducible artifacts.
- IDE state, linked worktrees, package caches, dependency folders, and build
  outputs already covered by the repository ignore rules.

If a binary PowerPoint fixture becomes necessary later, it must be introduced
with a narrowly scoped `.gitignore` exception and a test that explains why the
fixture cannot be generated.

## Documentation Placement

The plugin design and implementation plan remain under `docs/superpowers/`
because they describe repository functionality.

The two ZYNQ-specific documents move to the local ignored archive:

```text
docs/superpowers/specs/2026-06-12-zynq-glass-substrate-validation-deck-design.md
  -> outputs/archive-docs/zynq-glass-substrate-validation-deck/2026-06-12-zynq-glass-substrate-validation-deck-design.md

docs/superpowers/plans/2026-06-12-zynq-glass-substrate-validation-deck.md
  -> outputs/archive-docs/zynq-glass-substrate-validation-deck/2026-06-12-zynq-glass-substrate-validation-deck.md
```

The archived files retain their existing filenames and contents. Their tracked
copies are removed from the clean branch and from the history that will be
pushed.

README gains a concise repository artifact policy so contributors can decide
what belongs in Git without reading this design record.

## PowerPoint Cleanup

Remove these tracked, reproducible decks from the repository:

```text
mermaid-native.pptx
mindmap.pptx
專案核心總覽 Flowchart.pptx
samples/flowchart.pptx
samples/軟體架構圖.pptx
samples/AI 推論與控制流程圖.pptx
```

Also move generated `.pptx` files found under `out/` and
`src/Mermaid2Pptx.Web/out/` to the Windows Recycle Bin. Do not delete any
PowerPoint files under `outputs/` or `.worktrees/`.

Tracked files remain recoverable from the local safety branch and existing Git
objects. Recycled untracked files remain recoverable from the Recycle Bin.

## Git History Strategy

Deleting the ZYNQ documents in a new commit would still publish their contents
in the two earlier commits. The clean history therefore starts at
`origin/master` and replays only:

1. The nine plugin implementation commits.
2. This repository hygiene design commit.
3. The repository hygiene implementation commit.

Before moving the local `master` reference, create a local safety branch at the
current commit. The safety branch is not pushed.

The reconstructed history must be a fast-forward of `origin/master`. After
verification, update local `master` to the reconstructed tip and push it to
`origin/master`.

## Architecture And Ownership

This cleanup introduces no production classes, services, or utilities.
Repository artifact ownership remains explicit:

- `.gitignore` owns exclusion policy.
- `README.md` owns contributor-facing repository guidance.
- `docs/superpowers/` owns approved repository and feature design records.
- `outputs/` owns local deliverables and project-specific archives.
- Existing source, QA, and plugin layers remain unchanged.

No business logic moves into I/O code, no dependency direction changes, and no
new runtime coupling is introduced.

## Files To Modify

- `.gitignore`
- `README.md`
- `docs/superpowers/specs/2026-07-24-repository-hygiene-design.md`
- The six tracked `.pptx` paths listed above
- The two ZYNQ design/plan paths, archived locally under `outputs/archive-docs/`

No converter, Web UI, QA, test, or plugin production code should change.

## Verification Strategy

Before push:

1. Confirm `outputs/` is ignored and still present.
2. Confirm no tracked `.pptx` remains.
3. Confirm no `.pptx` remains outside `outputs/` and `.worktrees/`.
4. Confirm both ZYNQ documents exist in the local archive with matching SHA-256
   hashes.
5. Confirm the clean history does not contain either ZYNQ document path or the
   two ZYNQ-only commits.
6. Run plugin validation and plugin tests.
7. Run the Mermaid2Pptx xUnit suite.
8. Run `git diff --check`.
9. Confirm the clean branch is a fast-forward of `origin/master`.
10. Push only after every check passes.

## Success Criteria

- The team receives the complete portable plugin and its source documentation.
- `outputs/` stays intact locally and is not published.
- Generated PowerPoint decks are absent from tracked repository content.
- ZYNQ-specific documents are absent from pushed history but preserved locally.
- The active linked worktree is untouched.
- Tests and validation pass on the reconstructed history.
