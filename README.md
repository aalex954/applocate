# applocate

[![build-test-release](https://github.com/aalex954/applocate/actions/workflows/build-release.yml/badge.svg)](https://github.com/aalex954/applocate/actions/workflows/build-release.yml)

Windows 11 CLI to locate application install directories, executables, and (in progress) config/data paths. Emits deterministic JSON (plus CSV/text). Core discovery, indexing, ranking scaffold, and baseline tests are now in place.

## Features (Snapshot)
| Area | Implemented | Notes |
|------|-------------|-------|
| Registry uninstall | Yes | HKLM/HKCU + WOW6432Node |
| App Paths | Yes | HKLM/HKCU App Paths; exe + optional Path dir |
| Start Menu shortcuts | Yes | .lnk resolution (COM) user + common |
| Processes | Yes | Running processes; synergy evidence |
| PATH search | Yes | where.exe + PATH scan |
| MSIX / Store | Yes | PowerShell enumeration + env fake provider |
| Services & Tasks | Yes | ImagePath + scheduled task parsing |
| Heuristic FS scan | Yes | Bounded depth/time roots |
| Index cache | Yes | Known-miss short‑circuit |
| Ranking | Phase 2 | Span compactness, noise penalties, refined diminishing returns |
| Config/Data rules | Yes | YAML rule pack (≥50 apps) |
| Existence filtering | Yes | Drops non-existent paths (live + cache sanitize) |
| Evidence emission | Yes | Optional via --evidence |
| Snapshot tests | Yes | Verify deterministic outputs |
| Single-file publish | Yes | Win x64/ARM64 + SBOM |
| Plugin system | Pending | Data-only aliases/rules planned |

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
	--strict                   Disable fuzzy / alias matching
	--user | --machine         Scope filters
	--all                      Return ALL hits (no per-type collapsing)
	--exe | --install-dir | --config | --data  Type filters (any combination)
	--running                  Include running process enumeration
	--pid <n>                  Target specific process id (implies --running)
	--package-source           Show package type & source list in text/CSV output
	--threads <n>              Max parallel source queries (default=min(logical CPU,16))
	--trace                    Per-source timing diagnostics (stderr; prefix [trace])
	--evidence                 Include evidence dictionary (if available)
	--refresh-index            Ignore cached record
	--clear-cache              Delete index file before running (rebuild cache)
	--index-path <file>        Override index file path
	--timeout <sec>            Per-source soft timeout (default 5)
	--no-color                 Disable ANSI color in text output
	--verbose                  Verbose diagnostics (warnings)
	--help                     Show help
	--                         Treat following tokens as literal query
```

Default behavior (without `--all`): results are collapsed to the single best hit per type (`exe`, `install_dir`, `config`, `data`) using confidence, then tie‑broken by scope (machine over user) and evidence richness. Use `--all` to inspect every distinct hit (useful for debugging ranking or seeing alternate install roots).

Planned / not yet implemented flags from original design (roadmap): `--fuzzy` (explicit enable), `--elevate` / `--no-elevate`. These remain on the backlog and are intentionally absent from current binary.

Exit codes: 0 (results), 1 (no matches), 2 (argument error), 3 (permission), 4 (internal).

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
applocate sample --json --refresh-index
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
- Sources (real): Registry Uninstall (HKLM/HKCU, WOW6432Node), App Paths, Start Menu shortcuts (.lnk resolution), Running Processes, PATH search (`where` fallback), Services & Scheduled Tasks (image path extraction), MSIX/Store packages (Appx), Heuristic filesystem scan (bounded depth/timeout, curated token filters).
- Evidence & Merge: Dedup + union of `Source` arrays and merged evidence key/value sets.
- Ranking: token & fuzzy coverage (Jaccard + collapsed substring), exact exe/dir boosts, alias equivalence (embedded dictionary), evidence synergy (shortcut+process), diminishing returns on multi-source, path quality penalties; final score clamped [0,1].
- Indexing: On-disk JSON (`%LOCALAPPDATA%/AppLocate/index.json`) with environment hash invalidation + empty-cache short‑circuit (cached known-miss returns exit 1 instantly). `--index-path`, `--refresh-index` supported.
- Argument parsing: Manual robust multi-word parsing + `--` sentinel, validation for numeric options, custom help text (uses `System.CommandLine` only for usage surface).
- Output formats: text (color-aware), JSON, CSV.
- Rules engine: lightweight YAML (subset) parser expands config/data hits (VSCode, Chrome examples) before ranking.
- Comprehensive automated test suite (see Tests section).

In Progress / Next Focus:
- Ranking refinement (alias weighting, fuzzy distance scoring, multi-source diminishing returns calibration).
- Acceptance scenario fixtures (VSCode, Chrome, portable app, MSIX) with golden expectations.
- Config/Data heuristics expansion via YAML rule pack (currently minimal seeds in `rules/apps.default.yaml`).
- Performance tuning (parallel source fan-out, thread cap, profiling cold vs warm index hits).

Upcoming Backlog:
- Plugin loading for alias/rule packs (data-only).
- DI/registration refactor for sources.
- Expanded YAML rules (50+ popular apps) & tests.
- CSV / evidence output tests and JSON contract schema doc.
- Portable app fixture harness (synthetic directory & shortcut).
- Packaging: single-file trimmed exe, optional PowerShell module export.
- CI enhancements (matrix build win-x64/arm64, cache index test artifacts, code signing optional).
- Performance benchmarks & regression guard.

## Project Layout
```
src/AppLocate.Core    # Domain models, abstractions, placeholder sources, ranking & rules stubs
src/AppLocate.Cli     # CLI entry point (manual parsing placeholder)
tests/*.Tests         # xUnit test projects
rules/apps.default.yaml  # Sample rule file (placeholder)
build/publish.ps1     # Single-file publish script (win-x64 / win-arm64)
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
Exit codes: 0 (results found), 1 (no matches), 2 (bad arguments/validation error).

## Publish Single-File
```pwsh
pwsh ./build/publish.ps1 -X64 -Arm64 -Configuration Release
```
Artifacts land under `./artifacts/<rid>/`.

Each published RID artifact now includes a CycloneDX SBOM file (`sbom-<rid>.json`) listing dependency components for supply-chain transparency.

## Roadmap (abridged – current)
Completed / Phase 1 Foundation:
- [x] Reintroduce `System.CommandLine` + full option set
- [x] Initial ranking heuristics (phase 1)
- [x] JSON indexing cache (write-through)
- [x] Environment hash invalidation & empty-cache short‑circuit
- [x] Discovery sources (registry uninstall, App Paths, start menu shortcuts, processes, PATH, services/tasks, MSIX/Store, heuristic FS)
- [x] Golden snapshot tests (Verify) for core queries
- [x] Deterministic CLI argument validation tests
- [x] YAML rules engine (phase 1 subset) for config/data expansion
- [x] Ranking calibration pass (alias & fuzzy weighting baseline)
- [x] Composite cache key (query + flags) variant separation
- [x] Legacy cache pruning (removal of pre-composite keys)
- [x] Cache short-circuit path applies type filters & package-source formatting
- [x] `--package-source` output integration
- [x] `--clear-cache` flag (index reset)
- [x] Acceptance scenario scaffolding (VSCode, Chrome, portable app; MSIX placeholder)
- [x] PowerShell module wrapper (Invoke-AppLocate / Get-AppLocateJson)
- [x] Rule pack expansion (≥50 apps coverage)

In Progress / Near Term:
- [x] Running process acceptance scenario (`--running` live capture)
- [ ] Expanded config/data heuristics acceptance scenarios
- [x] Ranking refinement (phase 2: distance weighting, diminishing returns tuning, span scoring)
- [ ] Performance tuning (parallel scheduling, cold vs warm benchmarks)
- [ ] Evidence output stabilization & selective evidence emission tests
- [ ] Plugin loading (data-only alias & rule packs)
- [ ] DI/registration refactor for sources (clean injection seams)
- [ ] JSON schema contract & versioning documentation
- [ ] Benchmark harness (cold vs warm index, thread scaling, source timing)

Backlog / Later:
- [ ] Existence filtering layer (live + cache sanitize)
- [ ] Rule pack ≥50 apps finalized with tests
- [ ] Advanced ranking ML/learned weights experiment (optional)
- [ ] CI matrix (x64/ARM64), optional code signing & SBOM pipeline polish
- [ ] PowerShell module publishing & gallery packaging
- [ ] Trimming / single-file size optimization, ReadyToRun evaluation
- [ ] Plugin pack distribution format (zip/yaml catalog)
- [ ] Elevation strategy (`--elevate` / `--no-elevate`) & privileged source gating
- [ ] Additional package manager adapters (Chocolatey, Scoop, Winget integration improvements)

## Tests

Current summary: 69 total (68 passing, 1 skipped) – includes running process acceptance test.

Categories:
- Core & Models: Validate `AppHit` serialization and JSON determinism.
- Ranking: Tokenization, fuzzy scoring, boosts, penalties.
- CLI Deterministic: Argument parsing, validation, exit codes.
- Snapshot (Verify): Golden projections with volatile fields stripped.
- Rules Parsing: YAML subset correctness & expansion.
- Synthetic Acceptance: VSCode (query "code"), Portable app, Chrome, MSIX fake provider.
- Skipped: One placeholder scenario reserved for future expansion.
- Cache & Index Variants: Composite key separation, short-circuit behavior, pruning, clear cache flag.

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
- Always pass `--refresh-index` to avoid stale index hits or cached misses.
- Override `LOCALAPPDATA`, `APPDATA`, `PATH` to point to temp fixtures.
- Inject MSIX packages via `APPLOCATE_MSIX_FAKE` (JSON array) for deterministic enumeration.
- Use .lnk shortcuts to exercise Start Menu + evidence synergy.

Adding acceptance scenarios:
1. Build temp layout & dummy exe(s).
2. Optionally add rule entries in `rules/apps.default.yaml` for config/data.
3. Invoke CLI with `--refresh-index`; assert required hit types & confidence ≥0.8.
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

## Indexing
The tool maintains a lightweight JSON cache at `%LOCALAPPDATA%/AppLocate/index.json` using a composite key including query + relevant flag state. Each record stores:
- Timestamp of last refresh.
- List of scored entries with first/last seen timestamps.

Composite key format example:
```
code|u0|m0|s0|r0|p0|te0|ti0|tc0|td0|c0.00
```
Segments: query | user flag | machine flag | strict | running | pid | exe filter | install filter | config filter | data filter | confidence threshold.

Pruning removes legacy single-segment keys on load (logged with `--verbose`).

Current behavior:
1. (Optional) `--clear-cache` deletes index file before any load.
2. Load index & compute environment hash; invalidate if mismatch.
3. Prune legacy keys; persist if mutations.
4. If composite key record exists and not `--refresh-index`, apply confidence & type filters then emit (short‑circuit).
5. If cached empty record (known miss) and not `--refresh-index`, exit 1.
6. Otherwise query sources (parallel bounded), rank, apply filters, persist composite record.

Options:
`--index-path <file>` Override default index location.
`--refresh-index` Ignore any cached record for this query (even if fresh).
`--clear-cache` Remove the entire index file before running (full rebuild).

The cache is opportunistic; load/save failures are non-fatal (diagnosed with `--verbose`).

## Contributing
See `.github/copilot-instructions.md` for design/extension guidance. Keep `AppHit` schema backward compatible.

## Notes
- No network I/O, no executing discovered binaries.
- Keep JSON camelCase & deterministic ordering via source generator (`JsonContext`).
- Add XML docs gradually (warnings currently suppressed only by omission).
- Ranking: token coverage (+ up to 0.25), partial token Jaccard (+ up to 0.08 noise‑scaled), span compactness (+0.14) with noise penalties for excessive non‑query tokens (up to -0.12), exact filename (+0.30), alias equivalence (+0.22) vs evidence alias (+0.14), fuzzy filename similarity (Levenshtein scaled + up to 0.06), evidence boosts (shortcut/process + synergy, where, dir/exe matches), harmonic multi-source diminishing returns (cap +0.18), type baselines, and penalties (broken shortcut, temp/installer/cache paths). Scores clamped to [0,1].

---
This README reflects the discovery + indexing + baseline test milestone; update with each subsequent milestone (ranking calibration, rules, performance, packaging).
