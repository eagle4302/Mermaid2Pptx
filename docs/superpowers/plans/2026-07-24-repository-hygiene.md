# Repository Hygiene Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish a source-focused Mermaid2Pptx repository while preserving local deliverables, removing reproducible PPTX artifacts, and excluding the ZYNQ-specific documents from pushed history.

**Architecture:** `.gitignore` owns artifact exclusion, `README.md` documents the contributor policy, and `outputs/` owns ignored local deliverables and archives. Cleanup is recoverable through the Windows Recycle Bin and a local safety branch; the public history is reconstructed as a fast-forward from `origin/master` using only the plugin and repository-hygiene commits.

**Tech Stack:** Git, PowerShell 7, Windows Recycle Bin APIs, .NET 8, Pester, Python plugin validators

## Global Constraints

- Keep every existing file under `outputs/`; add the directory to `.gitignore`.
- Do not modify or delete anything under `.worktrees/`.
- Do not push the local safety branch.
- Do not use `git reset --hard`, force-push, or overwrite a remote update.
- Generated `.pptx` files are never committed.
- Delete only the six approved tracked decks and generated decks under `out/` or `src/Mermaid2Pptx.Web/out/`.
- Send generated PPTX files to the Windows Recycle Bin instead of permanently deleting them.
- Preserve the two ZYNQ documents locally with byte-identical SHA-256 hashes.
- Production converter, Web UI, QA, tests, and plugin code must not change.
- The final `master` must be a normal fast-forward of the fetched `origin/master`.

---

## File Map

- Modify: `.gitignore` — ignore local presentation projects and generated PowerPoint files.
- Modify: `README.md` — explain which repository artifacts contributors should commit.
- Delete from Git and archive locally:
  - `docs/superpowers/specs/2026-06-12-zynq-glass-substrate-validation-deck-design.md`
  - `docs/superpowers/plans/2026-06-12-zynq-glass-substrate-validation-deck.md`
- Delete from Git:
  - `mermaid-native.pptx`
  - `mindmap.pptx`
  - `專案核心總覽 Flowchart.pptx`
  - `samples/flowchart.pptx`
  - `samples/軟體架構圖.pptx`
  - `samples/AI 推論與控制流程圖.pptx`
- Preserve locally:
  - `outputs/archive-docs/zynq-glass-substrate-validation-deck/2026-06-12-zynq-glass-substrate-validation-deck-design.md`
  - `outputs/archive-docs/zynq-glass-substrate-validation-deck/2026-06-12-zynq-glass-substrate-validation-deck.md`

No runtime class, service, utility, or dependency changes are required.

---

### Task 1: Apply The Repository Artifact Policy

**Files:**
- Modify: `.gitignore`
- Modify: `README.md`
- Delete and archive: the two ZYNQ document paths from the File Map
- Delete: the six tracked PPTX paths from the File Map
- Delete locally: generated `*.pptx` files below `out/` and `src/Mermaid2Pptx.Web/out/`

**Interfaces:**
- Consumes: the approved repository-hygiene design and the current unpushed `master`.
- Produces: one cleanup commit whose tree contains no tracked PPTX or ZYNQ-specific document, plus byte-identical local archives under ignored `outputs/`.

- [ ] **Step 1: Record the pre-cleanup evidence**

Run:

```powershell
git status --short
git -c core.quotepath=false ls-files '*.pptx'
git worktree list --porcelain
git log --oneline origin/master..master
```

Expected:

- the only untracked entry is `outputs/`;
- exactly six tracked PPTX paths are listed;
- `.worktrees/zynq-validation-deck` is still registered at
  `ee3fe1a69aec2ad4bc4050fe6afe26879aba5203`;
- the eleven plugin/ZYNQ commits plus the repository-hygiene design and plan
  commits are local-only.

- [ ] **Step 2: Add focused ignore rules**

Append this exact block to `.gitignore` with `apply_patch`:

```gitignore

# Local presentation projects and generated PowerPoint decks
/outputs/
*.pptx
```

Do not add a broad rule for Markdown, HTML, Mermaid, SVG, PNG, or JSON because
those formats include reproducible sources and expected test assets.

- [ ] **Step 3: Document the contributor policy**

Add this section after `## Project Structure` in `README.md` with
`apply_patch`:

```markdown
## Repository Artifact Policy

Commit source code, tests, plugin files, documentation, and reproducible
Mermaid, HTML, or Markdown inputs.

Do not commit generated PowerPoint decks, QA output, build artifacts, or local
presentation projects. Generated `.pptx` files are ignored globally, and local
deliverables belong under `outputs/`. If a binary PPTX fixture is ever required,
add a narrowly scoped `.gitignore` exception together with the test that needs
it.
```

- [ ] **Step 4: Archive the two ZYNQ documents with hash verification**

Run this PowerShell from the repository root:

```powershell
$repositoryRoot = (Resolve-Path .).Path
$archiveRoot = Join-Path $repositoryRoot 'outputs\archive-docs\zynq-glass-substrate-validation-deck'
$archivePairs = @(
  @{
    Source = Join-Path $repositoryRoot 'docs\superpowers\specs\2026-06-12-zynq-glass-substrate-validation-deck-design.md'
    Destination = Join-Path $archiveRoot '2026-06-12-zynq-glass-substrate-validation-deck-design.md'
  },
  @{
    Source = Join-Path $repositoryRoot 'docs\superpowers\plans\2026-06-12-zynq-glass-substrate-validation-deck.md'
    Destination = Join-Path $archiveRoot '2026-06-12-zynq-glass-substrate-validation-deck.md'
  }
)

$beforeHashes = @{}
foreach ($pair in $archivePairs) {
  $resolvedSource = (Resolve-Path -LiteralPath $pair.Source).Path
  if (-not $resolvedSource.StartsWith((Join-Path $repositoryRoot 'docs\superpowers'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Archive source escaped docs/superpowers: $resolvedSource"
  }
  if (Test-Path -LiteralPath $pair.Destination) {
    throw "Archive destination already exists: $($pair.Destination)"
  }
  $beforeHashes[$pair.Source] = (Get-FileHash -Algorithm SHA256 -LiteralPath $pair.Source).Hash
}

New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null
foreach ($pair in $archivePairs) {
  Move-Item -LiteralPath $pair.Source -Destination $pair.Destination
  $afterHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $pair.Destination).Hash
  if ($afterHash -ne $beforeHashes[$pair.Source]) {
    throw "Archive hash mismatch: $($pair.Destination)"
  }
}
```

Expected: both sources disappear from `docs/superpowers/`, both archive files
exist, and neither SHA-256 value changes.

- [ ] **Step 5: Send only the approved PPTX files to the Recycle Bin**

Run:

```powershell
Add-Type -AssemblyName Microsoft.VisualBasic

$repositoryRoot = (Resolve-Path .).Path
$trackedDecks = @(
  'mermaid-native.pptx',
  'mindmap.pptx',
  '專案核心總覽 Flowchart.pptx',
  'samples\flowchart.pptx',
  'samples\軟體架構圖.pptx',
  'samples\AI 推論與控制流程圖.pptx'
) | ForEach-Object { Join-Path $repositoryRoot $_ }

$generatedRoots = @(
  (Join-Path $repositoryRoot 'out'),
  (Join-Path $repositoryRoot 'src\Mermaid2Pptx.Web\out')
)

$generatedDecks = foreach ($root in $generatedRoots) {
  if (Test-Path -LiteralPath $root) {
    Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.pptx'
  }
}

$candidates = @($trackedDecks) + @($generatedDecks.FullName)
$candidates = @($candidates | Where-Object { Test-Path -LiteralPath $_ } | Sort-Object -Unique)

foreach ($candidate in $candidates) {
  $resolvedCandidate = (Resolve-Path -LiteralPath $candidate).Path
  $isApprovedTrackedDeck = $trackedDecks -contains $resolvedCandidate
  $isGeneratedDeck = $false
  foreach ($root in $generatedRoots) {
    if ($resolvedCandidate.StartsWith("$root\", [StringComparison]::OrdinalIgnoreCase)) {
      $isGeneratedDeck = $true
      break
    }
  }
  if (-not ($isApprovedTrackedDeck -or $isGeneratedDeck)) {
    throw "Refusing to recycle out-of-scope file: $resolvedCandidate"
  }
  if ($resolvedCandidate.StartsWith((Join-Path $repositoryRoot 'outputs'), [StringComparison]::OrdinalIgnoreCase) -or
      $resolvedCandidate.StartsWith((Join-Path $repositoryRoot '.worktrees'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to recycle protected file: $resolvedCandidate"
  }
}

foreach ($candidate in $candidates) {
  [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile(
    $candidate,
    [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,
    [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin
  )
}
```

Expected: only the six tracked decks and generated decks in the two approved
output roots are recycled. `outputs/` and `.worktrees/` remain untouched.

- [ ] **Step 6: Verify the content policy before staging**

Run:

```powershell
git check-ignore -q outputs
if ($LASTEXITCODE -ne 0) { throw 'outputs/ is not ignored' }

$outsideProtectedRoots = @(
  Get-ChildItem -LiteralPath . -Recurse -File -Filter '*.pptx' |
    Where-Object {
      $_.FullName -notlike "$((Resolve-Path outputs).Path)\*" -and
      $_.FullName -notlike "$((Resolve-Path .worktrees).Path)\*"
    }
)
if ($outsideProtectedRoots.Count -ne 0) {
  $outsideProtectedRoots.FullName
  throw 'PPTX files remain outside protected local directories'
}

git status --short
```

Expected: no PPTX remains outside `outputs/` and `.worktrees/`; Git reports
only `.gitignore`, `README.md`, the two ZYNQ document deletions, and the six
tracked PPTX deletions.

- [ ] **Step 7: Stage exactly the cleanup paths and inspect the index**

Run:

```powershell
git add -- .gitignore README.md `
  'docs/superpowers/specs/2026-06-12-zynq-glass-substrate-validation-deck-design.md' `
  'docs/superpowers/plans/2026-06-12-zynq-glass-substrate-validation-deck.md' `
  'mermaid-native.pptx' `
  'mindmap.pptx' `
  '專案核心總覽 Flowchart.pptx' `
  'samples/flowchart.pptx' `
  'samples/軟體架構圖.pptx' `
  'samples/AI 推論與控制流程圖.pptx'

git diff --cached --check
git -c core.quotepath=false diff --cached --name-status
git -c core.quotepath=false ls-files '*.pptx'
```

Expected: the index contains two text modifications and eight deletions; the
tracked PPTX query returns no paths.

- [ ] **Step 8: Run repository verification**

Create the validator environment only if it is missing, then run:

```powershell
if (-not (Test-Path out/plugin-validator-venv/Scripts/python.exe)) {
  py -3 -m venv out/plugin-validator-venv
  & out/plugin-validator-venv/Scripts/python.exe -m pip install PyYAML
}

& out/plugin-validator-venv/Scripts/python.exe `
  C:/Users/User/.codex/skills/.system/skill-creator/scripts/quick_validate.py `
  plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations

& out/plugin-validator-venv/Scripts/python.exe `
  C:/Users/User/.codex/skills/.system/plugin-creator/scripts/validate_plugin.py `
  plugins/mermaid2pptx-presentations

pwsh -NoProfile -File tests/Plugin/Run-All.ps1
dotnet test tests/Mermaid2Pptx.Tests/Mermaid2Pptx.Tests.csproj
git diff --check
git diff --cached --check
```

Expected: both validators pass, all eight plugin tests pass, all 33 xUnit tests
pass, and both whitespace checks return exit code 0.

- [ ] **Step 9: Commit the verified cleanup**

Run:

```powershell
git commit -m "Clean generated presentation artifacts"
```

Expected: one commit containing only the approved policy changes and removals.

---

### Task 2: Reconstruct And Verify The Publishable History

**Files:**
- No working-tree content changes
- Create local branch: `codex/backup-before-repository-hygiene-20260724`
- Create temporary branch: `codex/repository-hygiene-clean`

**Interfaces:**
- Consumes: the verified cleanup commit and the nine plugin commits.
- Produces: a clean branch based directly on the latest fetched `origin/master`, with no ZYNQ-only commits or paths in its ancestry.

- [ ] **Step 1: Create a recoverable local safety branch**

Run:

```powershell
$backupBranch = 'codex/backup-before-repository-hygiene-20260724'
git show-ref --verify --quiet "refs/heads/$backupBranch"
if ($LASTEXITCODE -eq 0) {
  throw "Safety branch already exists: $backupBranch"
}
git branch $backupBranch master
git show --no-patch --oneline $backupBranch
```

Expected: the backup branch points at the cleanup commit and has no upstream.

- [ ] **Step 2: Fetch without changing the working tree**

Run:

```powershell
git fetch origin master
git status --short
git merge-base --is-ancestor origin/master master
if ($LASTEXITCODE -ne 0) {
  throw 'Fetched origin/master is not an ancestor of local master'
}
```

Expected: fetch succeeds, the working tree has no unignored changes, and the
old local `master` still descends from `origin/master`. If the ancestry check
fails, stop; do not rebase, reset, or push.

- [ ] **Step 3: Build the clean branch from the fetched remote**

Run:

```powershell
$backupBranch = 'codex/backup-before-repository-hygiene-20260724'
$cleanBranch = 'codex/repository-hygiene-clean'
git show-ref --verify --quiet "refs/heads/$cleanBranch"
if ($LASTEXITCODE -eq 0) {
  throw "Clean branch already exists: $cleanBranch"
}

$planCommit = git rev-list -1 --grep='^Document repository hygiene implementation plan$' $backupBranch
$cleanupCommit = git rev-list -1 --grep='^Clean generated presentation artifacts$' $backupBranch
if (-not $planCommit -or -not $cleanupCommit) {
  throw 'Could not resolve the plan or cleanup commit'
}

$publishCommits = @(
  '10e8458',
  'd430834',
  'ae8da09',
  '2f06aa9',
  '3aa301c',
  '9b6a8dd',
  '0924348',
  '3fdaca9',
  'd2203f7',
  '4f82a95',
  $planCommit,
  $cleanupCommit
)

git switch -c $cleanBranch origin/master
if ($LASTEXITCODE -ne 0) { throw 'Could not create clean branch' }
git cherry-pick $publishCommits
if ($LASTEXITCODE -ne 0) {
  throw 'Cherry-pick failed; stop and preserve the safety branch'
}
```

Expected: twelve commits are replayed in order on top of `origin/master`.

- [ ] **Step 4: Prove the unwanted history is absent**

Run:

```powershell
$forbiddenPaths = @(
  'docs/superpowers/specs/2026-06-12-zynq-glass-substrate-validation-deck-design.md',
  'docs/superpowers/plans/2026-06-12-zynq-glass-substrate-validation-deck.md'
)

foreach ($path in $forbiddenPaths) {
  $history = @(git log --format='%H %s' origin/master..HEAD -- $path)
  if ($history.Count -ne 0) {
    $history
    throw "Forbidden path remains in publishable history: $path"
  }
}

$forbiddenSubjects = @(
  'Add ZYNQ validation deck design spec',
  'Add ZYNQ deck implementation plan'
)
foreach ($subject in $forbiddenSubjects) {
  $matches = @(git log --format='%s' origin/master..HEAD | Where-Object { $_ -eq $subject })
  if ($matches.Count -ne 0) {
    throw "Forbidden commit remains in publishable history: $subject"
  }
}

$trackedDecks = @(git -c core.quotepath=false ls-files '*.pptx')
if ($trackedDecks.Count -ne 0) {
  $trackedDecks
  throw 'Tracked PPTX files remain'
}

git merge-base --is-ancestor origin/master HEAD
```

Expected: both forbidden path histories and both forbidden subjects are absent,
no PPTX is tracked, and the clean branch is a fast-forward of `origin/master`.

- [ ] **Step 5: Re-run the full verification on the reconstructed history**

Run:

```powershell
& out/plugin-validator-venv/Scripts/python.exe `
  C:/Users/User/.codex/skills/.system/skill-creator/scripts/quick_validate.py `
  plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations

& out/plugin-validator-venv/Scripts/python.exe `
  C:/Users/User/.codex/skills/.system/plugin-creator/scripts/validate_plugin.py `
  plugins/mermaid2pptx-presentations

pwsh -NoProfile -File tests/Plugin/Run-All.ps1
dotnet test tests/Mermaid2Pptx.Tests/Mermaid2Pptx.Tests.csproj
git diff --check
git status --short
```

Expected: both validators pass, all plugin and xUnit tests pass, whitespace is
clean, and the working tree has no unignored changes.

---

### Task 3: Update Master And Push The Clean Fast-Forward

**Files:**
- No content changes
- Update local branch reference: `master`
- Delete temporary local branch after it is redundant
- Push branch: `origin/master`

**Interfaces:**
- Consumes: the fully verified clean branch and unchanged fetched remote base.
- Produces: matching local and remote `master` tips while retaining the unpushed local safety branch.

- [ ] **Step 1: Move local master to the verified clean tip without a hard reset**

Run:

```powershell
$cleanBranch = 'codex/repository-hygiene-clean'
$cleanTip = git rev-parse $cleanBranch
git branch -f master $cleanTip
git switch master
git branch -d $cleanBranch
```

Expected: `master` points at the verified clean tip, the temporary branch is
deleted normally, and the safety branch still points at the original history.

- [ ] **Step 2: Perform the final pre-push gate**

Run:

```powershell
git fetch origin master
git merge-base --is-ancestor origin/master master
if ($LASTEXITCODE -ne 0) {
  throw 'Remote advanced incompatibly; refusing to push'
}

$behindAhead = git rev-list --left-right --count origin/master...master
$behindAhead
git status --short
git -c core.quotepath=false ls-files '*.pptx'
git branch -vv
```

Expected:

- the left/behind count is `0`;
- `master` is ahead only by the twelve approved commits;
- no unignored working-tree changes or tracked PPTX files exist;
- the safety branch has no upstream and will not be pushed.

- [ ] **Step 3: Push without force**

Run:

```powershell
git push origin master
```

Expected: a normal fast-forward push succeeds. If rejected, stop and inspect the
remote; do not retry with `--force`.

- [ ] **Step 4: Verify the remote and protected local content**

Run:

```powershell
git fetch origin master
$localTip = git rev-parse master
$remoteTip = git rev-parse origin/master
if ($localTip -ne $remoteTip) {
  throw "Local/remote mismatch: $localTip != $remoteTip"
}

if (-not (Test-Path -LiteralPath outputs)) {
  throw 'outputs/ was not preserved'
}
git check-ignore -q outputs
if ($LASTEXITCODE -ne 0) {
  throw 'outputs/ is no longer ignored'
}

$archiveRoot = 'outputs/archive-docs/zynq-glass-substrate-validation-deck'
$archiveFiles = @(Get-ChildItem -LiteralPath $archiveRoot -File)
if ($archiveFiles.Count -ne 2) {
  $archiveFiles.FullName
  throw 'Expected exactly two archived ZYNQ documents'
}

$worktreeRecord = @(git worktree list --porcelain)
if (-not ($worktreeRecord -contains 'HEAD ee3fe1a69aec2ad4bc4050fe6afe26879aba5203')) {
  $worktreeRecord
  throw 'The linked ZYNQ worktree HEAD changed'
}

git status --short --ignored
git log --oneline origin/master..master
```

Expected: local and remote tips match, `outputs/` and both archives remain
present and ignored, the linked worktree HEAD is unchanged, and there are no
unpushed commits.

---

## Completion Checklist

- [ ] `outputs/` is intact and ignored.
- [ ] Both ZYNQ documents are archived locally with unchanged hashes.
- [ ] No PPTX is tracked.
- [ ] No PPTX remains outside `outputs/` or `.worktrees/`.
- [ ] The two ZYNQ-only commits and document paths are absent from remote history.
- [ ] Plugin validators pass.
- [ ] Eight plugin tests pass.
- [ ] Thirty-three xUnit tests pass.
- [ ] `git diff --check` passes.
- [ ] The active linked worktree remains unchanged.
- [ ] `master` is pushed by normal fast-forward and matches `origin/master`.
- [ ] The safety branch exists only locally.
