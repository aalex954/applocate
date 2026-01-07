# Copilot Instructions — AppLocate

## Project Overview

**AppLocate** is a Windows CLI tool that finds application installations, executables, config files, and data directories. It queries multiple sources in parallel, ranks results by confidence, and outputs structured JSON/CSV/text.

**See [README.md](../README.md) for full capabilities, CLI flags, and usage examples.**

---

## Architecture

```
src/
├── AppLocate.Core/           # Library: sources, ranking, models, rules
│   ├── Abstractions/         # ISource, ISourceRegistry, IAmbientServices
│   ├── Sources/              # One file per data source
│   ├── Ranking/              # Ranker, AliasCanonicalizer
│   ├── Rules/                # YAML rule pack loader
│   ├── Models/               # AppHit, enums, ScoreBreakdown
│   └── Indexing/             # Optional on-disk cache (IndexStore)
└── AppLocate.Cli/            # Console entry point, output formatting
```

### Key Patterns

| Concept | Location | Notes |
|---------|----------|-------|
| Data sources | `Sources/*.cs` | Each implements `ISource`; registered via `SourceRegistryBuilder` |
| Ranking | `Ranking/Ranker.cs` | Token matching, evidence synergy, penalties |
| App rules | `rules/apps.default.yaml` | 147-app rule pack for config/data paths |
| JSON output | `JsonContext.cs` | Source-generated for AOT compatibility |
| Snapshots | `tests/.../Snapshots/` | Verify CLI output stability |

---

## Adding a New Source

1. Create `Sources/FooSource.cs` implementing `ISource`:
   ```csharp
   public sealed class FooSource : ISource {
       public string Name => nameof(FooSource);
       public async IAsyncEnumerable<AppHit> QueryAsync(
           string query, SourceOptions options, [EnumeratorCancellation] CancellationToken ct) {
           // Yield AppHit values; swallow per-item errors; respect ct
       }
   }
   ```
2. Register it in `SourceRegistryBuilder.CreateDefault()`.
3. Add tests in `tests/AppLocate.Core.Tests/`.

---

## Code Guidelines

### DO

| Guideline | Why |
|-----------|-----|
| Write incremental PRs — one source or feature per PR | reviewability |
| Add/update snapshot tests when CLI output format changes | regression |
| Run `dotnet test` before suggesting changes | CI |
| Use `async IAsyncEnumerable` for sources; yield as discovered | perf |
| Keep `AppHit` stable — additive changes only | compat |
| Include evidence conditionally (respect `options.IncludeEvidence`) | perf |
| Expand environment variables and resolve `.lnk` targets | correctness |
| Prefer 64-bit paths when both architectures are present | consistency |

### AVOID

| Guideline | Why |
|-----------|-----|
| New P/Invoke signatures if an existing NuGet package solves it | portability |
| Global static state (sources receive dependencies via constructor) | testability |
| Altering JSON property ordering (breaks downstream consumers) | compat |
| Throwing from `QueryAsync` — swallow and log per-item errors | resilience |
| Direct file traversal in `WindowsApps` (requires elevation) | least-privilege |
| Network calls — all discovery is local-only | security |
| Executing discovered binaries | security |

---

## Testing

```pwsh
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~RankingTests"

# Update snapshots (when intentional output changes occur)
# Set SNAPSHOTTER_UPDATE=1 or delete .snap file and re-run
```

Snapshot tests live in `tests/AppLocate.Cli.Tests/Snapshots/` — review diffs carefully.

---

## Building & Publishing

```pwsh
# Debug build
dotnet build

# Release single-file publish (x64)
dotnet publish src/AppLocate.Cli -c Release -r win-x64 --self-contained

# Full release script (x64 + ARM64 + PowerShell module)
./build/publish.ps1
```

Artifacts go to `artifacts/`.

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Results found (or `--help` shown) |
| 1 | No matches |
| 2 | Argument/validation error |

---

## Security Principles

- Least privilege by default — no admin required for core features
- Never execute target binaries
- Sanitize output to avoid control characters
- No telemetry; no network access
