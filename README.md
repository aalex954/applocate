dotnet build
dotnet test
# applocate

Windows 11 CLI (scaffold) to locate application install directories, executables, and future config/data paths. Output will be deterministic JSON (plus CSV/text) once sources are implemented.

## Current Status
Baseline only – no real hits yet.
- Core contract: `AppHit` record & enums (stable).
- Placeholder sources in `Sources/` all return zero results.
- Ranking & rules engines are stubs.
- CLI uses a minimal manual argument parser (will migrate to `System.CommandLine`).
- Tests: 2 passing placeholder xUnit tests.

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

Run (will currently produce no hits):
```pwsh
dotnet run --project src/AppLocate.Cli -- vscode --json
```
Exit codes (scaffold): 0 (reserved for future hits), 1 (no hits), 2 (bad args).

## Publish Single-File
```pwsh
pwsh ./build/publish.ps1 -X64 -Arm64 -Configuration Release
```
Artifacts land under `./artifacts/<rid>/`.

## Roadmap (abridged)
- [ ] Reintroduce `System.CommandLine` with full option set.
- [ ] Implement sources: RegistryUninstall, AppPaths, StartMenuShortcut, Process, PathSearch, MsixStore, HeuristicFs.
- [ ] Aggregation + de-dup + evidence accumulation.
- [ ] Fuzzy token-set ranking & scoring bonuses/penalties.
- [ ] YAML rules engine → derive Config/Data hits.
- [ ] Golden JSON tests (Verify) + ranking tests.
- [ ] Performance: parallel source execution, timeouts, trimming & ReadyToRun tuning.

## Contributing
See `.github/copilot-instructions.md` for design/extension guidance. Keep `AppHit` schema backward compatible.

## Notes
- No network I/O, no executing discovered binaries.
- Keep JSON camelCase & deterministic ordering via source generator (`JsonContext`).
- Add XML docs gradually (warnings currently suppressed only by omission).

---
This README reflects the scaffold state; update alongside each milestone.
