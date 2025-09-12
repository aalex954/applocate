dotnet build
dotnet test
# applocate

Windows 11 CLI to locate application install directories, executables, and (in progress) config/data paths. Emits deterministic JSON (plus CSV/text). Core discovery, indexing, ranking scaffold, and baseline tests are now in place.

## Current Status
Foundation milestone reached – all primary discovery sources implemented with real enumeration logic.

Implemented:
- Core contract: `AppHit` record & enums (stable, JSON source generator context in `JsonContext`).
- Sources (real): Registry Uninstall (HKLM/HKCU, WOW6432Node), App Paths, Start Menu shortcuts (.lnk resolution), Running Processes, PATH search (`where` fallback), Services & Scheduled Tasks (image path extraction), MSIX/Store packages (Appx), Heuristic filesystem scan (bounded depth/timeout, curated token filters).
- Evidence & Merge: Dedup + union of `Source` arrays and merged evidence key/value sets.
- Ranking scaffold: normalization, token coverage, exact exe/dir boosts, source synergy placeholders, penalties; final score clamped [0,1].
- Indexing: On-disk JSON (`%LOCALAPPDATA%/AppLocate/index.json`) with environment hash invalidation + empty-cache short‑circuit (cached known-miss returns exit 1 instantly). `--index-path`, `--refresh-index` supported.
- Argument parsing: Manual robust multi-word parsing + `--` sentinel, validation for numeric options, custom help text (uses `System.CommandLine` only for usage surface).
- Output formats: text (color-aware), JSON, CSV.
- Tests: 17 passing (deterministic CLI behavior, snapshot JSON/text projections, indexing short‑circuit, ranking basics). Snapshots stabilized by projecting volatile fields.

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

## Roadmap (abridged – updated)
- [x] Reintroduce `System.CommandLine` + full option set
- [x] Initial ranking heuristics scaffold
- [x] JSON indexing cache (write-through)
- [x] Environment hash invalidation & empty-cache short‑circuit
- [x] Implement discovery sources (registry, start menu, processes, PATH, services/tasks, MSIX, heuristic FS)
- [x] Golden snapshot tests (Verify) for core queries
- [x] Deterministic CLI argument validation tests
- [ ] Ranking calibration & alias/fuzzy weighting
- [ ] YAML rules engine for config/data paths
- [ ] Rule pack expansion (≥50 apps)
- [ ] Acceptance scenario tests (VSCode, Chrome, portable, MSIX, --running)
- [ ] Performance tuning (parallelism, profiling, R2R/trim)
- [ ] Evidence output stabilization & selective inclusion tests
- [ ] Plugin loading (data-only) for aliases/rules
- [ ] DI/refactor of source registration
- [ ] PowerShell module export & packaging polish
- [ ] CI matrix (x64/ARM64), artifact signing (optional)
- [ ] JSON schema contract & versioning doc
- [ ] Benchmark suite (cold vs warm index, thread scaling)

## Indexing
The tool maintains a lightweight JSON cache at `%LOCALAPPDATA%/AppLocate/index.json`. Each normalized query string stores:
- Timestamp of last refresh.
- List of scored entries with first/last seen timestamps.

Current behavior:
1. Load index & compute environment hash (schema + env factors). If mismatched, invalidate.
2. If non-empty cached record and not `--refresh-index`, emit immediately (score filtering still applied).
3. If empty cached record (known miss) and not `--refresh-index`, exit 1 without querying sources.
4. Otherwise query sources in sequence (parallel fan-out pending), rank, persist snapshot record.

Options:
`--index-path <file>` Override default index location.
`--refresh-index` Force ignoring any cached record for this query (even if fresh).

The cache is opportunistic; failures to load/save are non-fatal (logged with `--verbose`).

## Contributing
See `.github/copilot-instructions.md` for design/extension guidance. Keep `AppHit` schema backward compatible.

## Notes
- No network I/O, no executing discovered binaries.
- Keep JSON camelCase & deterministic ordering via source generator (`JsonContext`).
- Add XML docs gradually (warnings currently suppressed only by omission).
- Ranking: token coverage (+ up to 0.25), exact filename (+0.30), evidence boosts (shortcut/process synergy, alias placeholder, where, dir/exe matches), multi-source diminishing returns (cap +0.18), type baselines, and penalties (broken shortcut, temp paths). Scores clamped to [0,1].

---
This README reflects the discovery + indexing + baseline test milestone; update with each subsequent milestone (ranking calibration, rules, performance, packaging).
