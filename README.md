dotnet build
dotnet test
# applocate

Windows 11 CLI (scaffold) to locate application install directories, executables, and future config/data paths. Output will be deterministic JSON (plus CSV/text) once sources are implemented.

## Current Status
Early prototype – some real signals (processes, registry uninstall, PATH, heuristics, shortcuts, services/tasks) but many sources still placeholders / stubs.
- Core contract: `AppHit` record & enums (stable).
- Sources: placeholder implementations (RegistryUninstall, AppPaths, StartMenu, Process, PATH search, Services/Tasks, heuristic FS, MSIX placeholder) – real enumeration still to be filled in.
- Ranking: token coverage, evidence boosts (shortcut/process synergy, where, dir/exe match, alias placeholder), multi-source diminishing returns, type baselines, penalties.
- Indexing: JSON cache per normalized query (`%LOCALAPPDATA%/AppLocate/index.json`) with `--index-path` & `--refresh-index`. Partial short‑circuit now implemented: cached non-empty hits above confidence threshold are reused; empty cached records still trigger refresh (planned improvement).
- CLI uses `System.CommandLine` (RC) for help, manual token extraction for stability.
- Tests: 15 passing xUnit tests (CLI options, ranking, indexing creation, cache short‑circuit). Count will grow with acceptance scenarios.

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

## Roadmap (abridged)
- [x] Reintroduce `System.CommandLine` with full option set.
- [x] Initial ranking heuristics & evidence model.
- [x] Basic JSON indexing cache (write-through).
- [ ] Implement sources: deepen Registry, StartMenu, Processes; complete MSIX & heuristics.
- [ ] Index read short‑circuit (empty-cache reuse + staleness heuristics; non-empty hit reuse implemented).
- [ ] Aggregation refinement (improved evidence merging & weighting rules).
- [ ] YAML rules engine → derive Config/Data hits.
- [ ] Golden JSON tests (Verify) + ranking tests.
- [ ] Performance: parallel source execution, timeouts, trimming & ReadyToRun tuning.
- [ ] Alias dictionary and fuzzy synonym expansion.

## Indexing
The tool maintains a lightweight JSON cache at `%LOCALAPPDATA%/AppLocate/index.json`. Each normalized query string stores:
- Timestamp of last refresh.
- List of scored entries with first/last seen timestamps.

Current behavior always refreshes then writes (ensuring new sources/ranking tweaks propagate). A future optimization will:
1. Load index.
2. If record not stale (e.g. < 24h) and not `--refresh-index`, return cached hits immediately.
3. Otherwise re-query sources then upsert.

Options:
`--index-path <file>` Override default index location.
`--refresh-index` Force ignoring any cached record for this query (even if fresh).

The cache is opportunistic; failures to load/save are silent and won't block queries.

## Contributing
See `.github/copilot-instructions.md` for design/extension guidance. Keep `AppHit` schema backward compatible.

## Notes
- No network I/O, no executing discovered binaries.
- Keep JSON camelCase & deterministic ordering via source generator (`JsonContext`).
- Add XML docs gradually (warnings currently suppressed only by omission).
- Ranking: token coverage (+ up to 0.25), exact filename (+0.30), evidence boosts (shortcut/process synergy, alias placeholder, where, dir/exe matches), multi-source diminishing returns (cap +0.18), type baselines, and penalties (broken shortcut, temp paths). Scores clamped to [0,1].

---
This README reflects the CLI refactor milestone; update alongside each future milestone.
