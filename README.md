<h1>applocate</h1>

[![build-test-release](https://github.com/aalex954/applocate/actions/workflows/build-release.yml/badge.svg)](https://github.com/aalex954/applocate/actions/workflows/build-release.yml)
[![CodeQL](https://github.com/aalex954/applocate/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/aalex954/applocate/actions/workflows/github-code-scanning/codeql)
<table>
<tr>
<td width="96" valign="top"><img src="assets/logo.svg" width="96" alt="applocate logo"/></td>
<td>


Windows 11 CLI to locate application install directories, executables, and config/data paths. Emits deterministic JSON (plus CSV/text). All primary discovery sources are implemented with ranking and a YAML rule pack (147 apps) for config/data expansion.
</td>
</tr>
</table>

## Features (Snapshot)
| Area | Implemented | Notes |
|------|-------------|-------|
| Registry uninstall | Yes | HKLM/HKCU + WOW6432Node |
| App Paths | Yes | HKLM/HKCU exe registration |
| Start Menu shortcuts | Yes | User + common .lnk resolution |
| Processes | Yes | Running process discovery |
| PATH search | Yes | PATH directories + where.exe |
| MSIX / Store | Yes | Appx package enumeration |
| Services & Tasks | Yes | Service + scheduled task binaries |
| Heuristic FS scan | Yes | Bounded depth/time scan |
| Scoop | Yes | User/global apps + manifests |
| Chocolatey | Yes | Machine-scope packages |
| WinGet | Yes | Package provenance |
| Ranking | Yes | Token matching, evidence synergy, penalties |
| Config/Data rules | Yes | 147-app YAML rule pack |
| Existence filtering | Yes | Filters non-existent paths |
| Evidence emission | Yes | Via --evidence flag |
| Single-file publish | Yes | Win x64/ARM64 + SBOM |
| Plugin system | Pending | Data-only aliases/rules |

## Usage
Basic:
```pwsh
applocate code
applocate "visual studio code" --json --limit 5
applocate chrome --csv --confidence-min 0.75 --evidence
```

Options (implemented CLI surface):
```
	<query>                    App name / alias / partial tokens
	--json | --csv | --text    Output (default text)
	--limit <N>                Max hits after filtering (applied after optional collapse)
	--confidence-min <f>       Filter threshold (0-1)
	--strict                   Deprecated/no-op (behavior integrated into default heuristics)
	--user | --machine         Scope filters
	--all                      Return ALL hits (no per-type collapsing)
	--exe | --install-dir | --config | --data  Type filters (any combination)
	--running                  Include running process enumeration
	--pid <n>                  Target specific process id (implies --running)
	--package-source           Show package type & source list in text/CSV output
	--threads <n>              Max parallel source queries (default=min(logical CPU,16))
	--trace                    Per-source timing diagnostics (stderr; prefix [trace])
	--evidence                 Include evidence dictionary (if available)
	--evidence-keys <k1,k2>    Only include specified evidence keys (implies --evidence)
	--score-breakdown          Show internal scoring component contributions per result
	--timeout <sec>            Per-source soft timeout (default 5)
	--no-color                 Disable ANSI color in text output
	--verbose                  Verbose diagnostics (warnings)
	--help                     Show help
	--                         Treat following tokens as literal query
```

Default behavior (without `--all`): results are collapsed to the single best hit per type (`exe`, `install_dir`, `config`, `data`) using confidence, then tie‑broken by scope (machine over user) and evidence richness. Use `--all` to inspect every distinct hit (useful for debugging ranking or seeing alternate install roots).

Planned / not yet implemented flags from original design (roadmap): `--fuzzy` (explicit enable), `--elevate` / `--no-elevate`. These remain on the backlog and are intentionally absent from current binary.

Exit codes:
- **0**: Results found, or help displayed (when run without arguments or with `--help`)
- **1**: No matches found
- **2**: Argument/validation error

## Evidence & Confidence
`--evidence` adds key/value provenance. Examples:
```jsonc
{"Shortcut":"C:/Users/u/.../Code.lnk"}
{"ProcessId":"1234","ExeName":"Code.exe"}
{"DisplayName":"Google Chrome","HasInstallLocation":"true"}
```
Confidence heuristic (phase 1): token & fuzzy coverage, exact exe/dir boosts, alias equivalence, evidence synergy (shortcut+process), multi-source diminishing returns, penalties (temp/broken). Scores ∈ [0,1].

## Environment Overrides
For deterministic tests:
* `APPDATA`, `LOCALAPPDATA`, `PROGRAMDATA`
* `PATH`
* `APPLOCATE_MSIX_FAKE` (JSON array of fake MSIX packages)

Example:
```pwsh
$env:APPLOCATE_MSIX_FAKE='[{"name":"SampleApp","family":"Sample.App_123","install":"C:/tmp/sample","version":"1.0.0.0"}]'
applocate sample --json
```

## Minimal JSON Hit Example
```jsonc
{
	"type": 1,
	"scope": 0,
	"path": "C:/Users/u/AppData/Local/Programs/Code/Code.exe",
	"version": null,
	"packageType": 3,
	"source": ["StartMenuShortcutSource","RegistryUninstallSource"],
	"confidence": 0.92,
	"evidence": {"Shortcut":"...Code.lnk","DisplayName":"Visual Studio Code"}
}
```
Fields are append-only; enum values only extend at tail.

## Versioning / Compatibility
* Pre-1.0: additive only (no breaking schema changes)
* 1.0+: semantic versioning
* Deterministic JSON ordering (source generator)
* Enum numeric values stable

## Security & Privacy
* No network or telemetry
* Does not execute discovered binaries
* Least privilege by default


<img width="537" height="413" alt="image" src="https://github.com/user-attachments/assets/45fe8756-6988-4091-af84-7097a45b2916" />


## Current Status 
Foundation milestone reached – all primary discovery sources implemented with real enumeration logic.

Implemented:
- Core contract: `AppHit` record & enums (stable, JSON source generator context in `JsonContext`).
- Sources (real): Registry Uninstall (HKLM/HKCU, WOW6432Node), App Paths, Start Menu shortcuts (.lnk resolution), Running Processes, PATH search (`where` fallback), Services & Scheduled Tasks (image path extraction), MSIX/Store packages (Appx), Heuristic filesystem scan (bounded depth/timeout, curated token filters), Scoop, Chocolatey, WinGet.
- Evidence & Merge: Dedup + union of `Source` arrays and merged evidence key/value sets.
- Ranking: token & fuzzy coverage (Jaccard + collapsed substring), exact exe/dir boosts, alias equivalence (embedded dictionary), evidence synergy (shortcut+process), diminishing returns on multi-source, path quality penalties; final score clamped [0,1].
- Package manager integration: Scoop (user/global), Chocolatey (machine-scope), WinGet (provenance via export). Sources gracefully no-op if the package manager is not installed.
- Argument parsing: Manual robust multi-word parsing + `--` sentinel, validation for numeric options, custom help text (uses `System.CommandLine` only for usage surface).
- Output formats: text (color-aware), JSON, CSV.
- Rules engine: lightweight YAML (subset) parser expands config/data hits (VSCode, Chrome examples) before ranking.
- Comprehensive automated test suite (see Tests section).

In Progress / Next Focus:
- PowerShell Gallery publishing.

Upcoming Backlog:
- PowerShell Gallery publishing.
- Code signing (optional).

## Project Layout
```
src/AppLocate.Core       # Domain models, abstractions, sources, ranking & rules engine
  ├─ Abstractions/       # Interfaces (ISource, ISourceRegistry, IAmbientServices)
  ├─ Models/             # AppHit, ScoreBreakdown, PathUtils, EvidenceKeys
  ├─ Sources/            # All discovery sources (Registry, AppPaths, StartMenu, Process, PATH, MSIX, Services, HeuristicFS, Scoop, Chocolatey, Winget)
  ├─ Ranking/            # Scoring logic, alias canonicalization
  ├─ Rules/              # YAML rule engine for config/data expansion
  └─ Indexing/           # Optional on-disk cache (IndexStore, IndexModels)
src/AppLocate.Cli        # CLI entry point with System.CommandLine + manual parsing
tests/AppLocate.Core.Tests   # Unit tests for ranking, rules, sources
tests/AppLocate.Cli.Tests    # CLI integration, acceptance, snapshot tests
rules/apps.default.yaml  # Config/data rule pack (147 apps)
build/publish.ps1        # Single-file publish script (win-x64 / win-arm64)
assets/                  # Logo (SVG, ICO)
AppLocate.psm1           # PowerShell module wrapper
```

## Quick Start
```pwsh
dotnet restore
dotnet build
dotnet test
```

Run (may produce limited or no hits depending on environment):
```pwsh
dotnet run --project src/AppLocate.Cli -- vscode --json
```
Exit codes: 0 (results or help), 1 (no matches), 2 (argument error). See [Usage](#usage) for details.

## Publish Single-File
```pwsh
pwsh ./build/publish.ps1 -X64 -Arm64 -Configuration Release
```
Artifacts land under `./artifacts/<rid>/`.

Each published RID artifact now includes a CycloneDX SBOM file (`sbom-<rid>.json`) listing dependency components for supply-chain transparency.

## Installation

### WinGet (Windows Package Manager)
```pwsh
winget install AppLocate.AppLocate
```

Stable releases are automatically submitted to the [Windows Package Manager Community Repository](https://github.com/microsoft/winget-pkgs). Pre-release versions (alpha, beta, rc) are not published to WinGet.

### Manual Download
Download the latest release from [GitHub Releases](https://github.com/aalex954/applocate/releases):
- `applocate-win-x64.zip` – Windows x64
- `applocate-win-arm64.zip` – Windows ARM64

Extract and add to your PATH, or run directly.

## Roadmap (abridged – current)
Completed / Phase 1 Foundation:
- [x] Reintroduce `System.CommandLine` + full option set
- [x] Initial ranking heuristics (phase 1)
// (Removed) JSON indexing cache & related invalidation (to be reconsidered later)
- [x] Discovery sources (registry uninstall, App Paths, start menu shortcuts, processes, PATH, services/tasks, MSIX/Store, heuristic FS)
- [x] Golden snapshot tests (Verify) for core queries
- [x] Deterministic CLI argument validation tests
- [x] YAML rules engine (phase 1 subset) for config/data expansion
- [x] Ranking calibration pass (alias & fuzzy weighting baseline)
- [x] `--package-source` output integration
- [x] Acceptance scenario scaffolding (VSCode, Chrome, portable app; MSIX placeholder)
- [x] PowerShell module wrapper (Invoke-AppLocate / Get-AppLocateJson)
// Indexing/cache layer removed; reconsider only if cold median >2s
// Completed refinements since v0.1.2
- [x] Evidence output stabilization & selective evidence emission tests
- [x] Alias canonicalization pipeline (query variants → canonical form)
- [x] Generic post-score noise filtering (uninstall/update/setup, cache/temp, docs/help)
- [x] Generic auxiliary service/host/updater/helper suppression
- [x] MSIX improvements: AppxManifest exe parsing, multi-token matching, WindowsApps path acceptance
- [x] App Execution Alias support via PATH search + alias canonicalization (e.g., `wt.exe` for Windows Terminal)
- [x] Score breakdown output (JSON) with detailed ranking signals
- [x] DI/registration refactor for sources (builder-based injection seams)

In Progress / Near Term:
- [ ] PowerShell Gallery publishing

Backlog / Later:
- [ ] Code signing for releases
- [ ] Elevation strategy (`--elevate` / `--no-elevate`) & privileged source gating

Completed (formerly backlog):
- [x] Package manager adapters (Scoop, Chocolatey, WinGet)
- [x] Rule pack ≥50 apps (now at 147)
- [x] CI matrix (x64/ARM64) with SBOM generation
- [x] Benchmark harness (exists in `benchmarks/`)
- [x] Parallel source execution with bounded concurrency
- [x] DI/registration refactor for sources
- [x] Single-file publish with ReadyToRun & compression

## Tests

Categories (representative):
- Core & Models: `AppHit` serialization, deterministic JSON shape.
- Ranking: tokenization, alias equivalence vs evidence alias, fuzzy distance, boosts/penalties, Steam auxiliary demotion, span/coverage.
- CLI Deterministic: argument parsing/validation, exit codes, type filters, `--package-source`, selective `--evidence-keys`.
- Snapshot (Verify): golden projections with volatile fields stripped.
- Rules Parsing: YAML subset correctness and expansion.
- Acceptance (synthetic): VS Code (code/vscode), portable app, MSIX fake provider; PATH alias discovery (oh‑my‑posh), running process (`--running`).
- Score breakdown: JSON `breakdown` presence and stability.

Run all tests:
```pwsh
dotnet test AppLocate.sln -c Release
```

Filter examples:
```pwsh
# Ranking tests only
dotnet test tests/AppLocate.Core.Tests --filter FullyQualifiedName~RankingTests

# Acceptance scenarios
dotnet test tests/AppLocate.Cli.Tests --filter FullyQualifiedName~Acceptance
```

Snapshots:
1. Make intentional change.
2. Run tests; inspect *.received.* files.
3. Approve by replacing .verified files (commit rationale).

Synthetic acceptance tips:
- Override `LOCALAPPDATA`, `APPDATA`, `PATH` to point to temp fixtures.
- Inject MSIX packages via `APPLOCATE_MSIX_FAKE` (JSON array) for deterministic enumeration.
- Use .lnk shortcuts to exercise Start Menu + evidence synergy.

Adding acceptance scenarios:
1. Build temp layout & dummy exe(s).
2. Optionally add rule entries in `rules/apps.default.yaml` for config/data.
3. Invoke CLI and assert required hit types & confidence ≥0.8.
4. Avoid dependence on real machine installs.

Contributor guidelines:
- Keep tests deterministic; no network or real software dependencies.
- Strip/ignore volatile data in snapshots (timestamps, absolute temp roots).
- Prefer suffix or logical assertions over full absolute path equality.
- Document ranking expectation changes in commits.
- Use `[Fact(Skip=..)]` sparingly with backlog reference.

Planned test expansions:
- Rule pack growth (≥50 apps) with fixtures.
- Live `--running` process capture scenario.
- Performance regression timing harness.

<!-- Indexing layer removed; section intentionally pruned. -->

## Contributing
See `.github/copilot-instructions.md` for design/extension guidance. Keep `AppHit` schema backward compatible.

## Notes
- No network I/O, no executing discovered binaries.
- Keep JSON camelCase & deterministic ordering via source generator (`JsonContext`).
- Add XML docs gradually (warnings currently suppressed only by omission).
- Ranking: token coverage (+ up to 0.25), partial token Jaccard (+ up to 0.08 noise‑scaled), span compactness (+0.14) with noise penalties for excessive non‑query tokens (up to -0.12), exact filename (+0.30), alias equivalence (+0.22) vs evidence alias (+0.14), fuzzy filename similarity (Levenshtein scaled + up to 0.06), evidence boosts (shortcut/process + synergy, where, dir/exe matches), harmonic multi-source diminishing returns (cap +0.18), type baselines, and penalties (broken shortcut, temp/installer/cache paths). Scores clamped to [0,1].

---
This README reflects the discovery + indexing + baseline test milestone; update with each subsequent milestone (ranking calibration, rules, performance, packaging).
