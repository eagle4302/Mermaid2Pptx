# PowerShell Compatibility And Installable CLI Plan

**Date:** 2026-07-27

## 1. Current Structure Summary

- `src/Mermaid2Pptx/Program.cs` owns command-line parsing and conversion
  orchestration.
- `src/Mermaid2Pptx/Mermaid2Pptx.csproj` builds an executable but is not
  currently packaged as a .NET tool.
- Plugin scripts restore and build the solution, then invoke the converter and
  QA projects through `dotnet run`.
- `Publish-Mermaid2PptxCandidate.ps1` uses the three-argument
  `System.IO.File.Move` overload. That overload is unavailable in the .NET
  Framework runtime used by Windows PowerShell 5.1.
- Plugin tests run under PowerShell 7 and therefore did not detect the
  PowerShell 5.1 publication failure.

## 2. Target Placement Decision

- Keep publication semantics inside `Publish-Mermaid2PptxCandidate.ps1`.
  Replace only the incompatible move overload; do not scatter filesystem
  behavior into the invocation wrapper.
- Declare `#requires -Version 5.1` in every public plugin script. PowerShell
  performs the version preflight before any conversion or filesystem work.
- Package the existing core executable as `Mermaid2Pptx.Tool` with the command
  name `mermaid2pptx`.
- Add `mermaid2pptx setup` to the core CLI. It delegates Chromium installation
  to the existing Microsoft.Playwright dependency.
- Have the initializer return the built core and QA executable paths. Wrappers
  invoke those executables directly instead of using `dotnet run`.
- Keep global .NET tool installation explicit. The wrapper continues to
  auto-prepare the repository-local build without mutating the user's global
  tool inventory.

## 3. Files To Modify

- `src/Mermaid2Pptx/Mermaid2Pptx.csproj`
- `src/Mermaid2Pptx/Program.cs`
- `tests/Mermaid2Pptx.Tests/CliOptionsTests.cs`
- `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/scripts/*.ps1`
- `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/SKILL.md`
- `plugins/mermaid2pptx-presentations/skills/mermaid2pptx-presentations/references/cli-and-qa.md`
- `plugins/mermaid2pptx-presentations/.codex-plugin/plugin.json`
- `tests/Plugin/Environment.Tests.ps1`
- `tests/Plugin/PluginContract.Tests.ps1`
- `tests/Plugin/PowerShellCompatibility.Tests.ps1`
- `tests/Plugin/CliPackaging.Tests.ps1`
- `README.md`

No parser, SVG mapper, DrawingML writer, Web UI, or domain model changes are
needed.

## 4. New Responsibilities

### `CliOptions`

- Recognize the `setup` command separately from conversion sources.
- Preserve all existing conversion and insertion parsing.

### `Program`

- Route `setup` to `Microsoft.Playwright.Program.Main(["install",
  "chromium"])`.
- Keep conversion orchestration unchanged.

### `Initialize-Mermaid2Pptx.ps1`

- Verify .NET 8, restore, and build as before.
- Resolve the platform-specific core and QA executable paths.
- Run the built `mermaid2pptx setup` command unless browser installation is
  skipped.
- Return executable paths to downstream wrappers.

### Invocation And QA Wrappers

- Invoke the built executables directly.
- Accept a test/development `-SkipBrowserInstall` switch while keeping
  automatic browser preparation as the default.

### Publisher

- Use the two-argument `File.Move` overload for no-overwrite moves.
- Preserve the existing same-directory atomic move, collision handling,
  rollback-backed replacement, and candidate preservation behavior.

## 5. Dependency Direction

```text
Skill workflow
  -> PowerShell invocation / QA adapters
      -> built mermaid2pptx and Mermaid2Pptx.Qa executables
          -> existing application and infrastructure layers

Installed mermaid2pptx .NET tool
  -> existing core CLI
      -> Microsoft.Playwright only for `setup` and browser-backed extraction
```

The core converter does not depend on plugin scripts. Plugin scripts depend on
the core executable paths returned by the initializer. No circular dependency
is introduced.

## 6. Testing Strategy

1. Add failing xUnit coverage for parsing `mermaid2pptx setup`.
2. Add a failing PowerShell compatibility test that:
   - verifies every public script declares PowerShell 5.1;
   - publishes through Windows PowerShell 5.1;
   - runs a complete wrapper conversion under Windows PowerShell 5.1.
3. Add a failing packaging test that:
   - packs `Mermaid2Pptx.Tool`;
   - installs it to a temporary tool path;
   - runs `mermaid2pptx setup`;
   - converts Mermaid through the installed command;
   - validates native-shape output.
4. Implement the minimum changes to make those tests pass.
5. Run plugin skill and manifest validators.
6. Run all plugin tests and the complete xUnit suite.
7. Run a direct PowerShell 5.1 smoke conversion and an installed-tool smoke
   conversion.
8. Confirm generated PPTX artifacts remain ignored and untracked.

## Acceptance Criteria

- Windows PowerShell 5.1 completes wrapper conversion and atomic publication.
- Older PowerShell versions fail before filesystem or build work.
- `dotnet tool install` produces a callable `mermaid2pptx` command.
- `mermaid2pptx setup` installs Playwright Chromium.
- Plugin wrappers contain no `dotnet run`.
- Existing fast audit, atomic publication, insertion, and full QA behavior
  remain intact.
- Plugin and xUnit suites pass without tracked PPTX or build artifacts.
