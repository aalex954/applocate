# applocate 

[![build-test-release](https://github.com/aalex954/applocate/actions/workflows/build-release.yml/badge.svg)](https://github.com/aalex954/applocate/actions/workflows/build-release.yml)

Windows 11 CLI to locate application install directories, executables, and (in progress) config/data paths. Emits deterministic JSON (plus CSV/text). Core discovery, indexing, ranking scaffold, and baseline tests are now in place.

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

## Roadmap (abridged – updated)
- [x] Reintroduce `System.CommandLine` + full option set
- [x] Initial ranking heuristics scaffold
- [x] JSON indexing cache (write-through)
- [x] Environment hash invalidation & empty-cache short‑circuit
- [x] Implement discovery sources (registry, start menu, processes, PATH, services/tasks, MSIX, heuristic FS)
- [x] Golden snapshot tests (Verify) for core queries
- [x] Deterministic CLI argument validation tests
- [x] Ranking calibration & alias/fuzzy weighting (phase 1)
- [x] YAML rules engine for config/data paths (phase 1 subset parser)
- [ ] Rule pack expansion (≥50 apps)
- [x] Acceptance scenario tests (VSCode (synthetic), Portable app (synthetic), Chrome synthetic, MSIX fake provider)
- [ ] Additional acceptance scenarios (--running live process capture, more config/data rule coverage)
- [ ] Performance tuning (parallelism, profiling, R2R/trim)
- [ ] Evidence output stabilization & selective inclusion tests
- [ ] Plugin loading (data-only) for aliases/rules
- [ ] DI/refactor of source registration
- [ ] PowerShell module export & packaging polish
- [ ] CI matrix (x64/ARM64), artifact signing (optional)
- [ ] JSON schema contract & versioning doc
- [ ] Benchmark suite (cold vs warm index, thread scaling)

## Tests

Current summary: 34 passing, 1 skipped.

Categories:
- Core & Models: Validate `AppHit` serialization and JSON determinism.
- Ranking: Tokenization, fuzzy scoring, boosts, penalties.
- CLI Deterministic: Argument parsing, validation, exit codes.
- Snapshot (Verify): Golden projections with volatile fields stripped.
- Rules Parsing: YAML subset correctness & expansion.
- Synthetic Acceptance: VSCode (query "code"), Portable app, Chrome, MSIX fake provider.
- Skipped: One placeholder scenario reserved for future expansion.

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
