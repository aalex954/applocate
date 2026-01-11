# Contributing to AppLocate

> **Accurate as of:** AppLocate v0.1.6

Thank you for your interest in contributing to AppLocate! This guide covers the development workflow, coding standards, and how to add new features.

## Quick Start

```bash
# Clone and build
git clone https://github.com/aalex954/applocate.git
cd applocate
dotnet restore
dotnet build

# Run tests
dotnet test

# Run locally
dotnet run --project src/AppLocate.Cli -- <query>
```

## Project Structure

```
applocate/
├── src/
│   ├── AppLocate.Core/        # Library: models, sources, ranking, rules
│   │   ├── Abstractions/      # ISource, ISourceRegistry interfaces
│   │   ├── Models/            # AppHit, HitType, Scope, ScoreBreakdown
│   │   ├── Sources/           # Discovery sources (one per file)
│   │   ├── Ranking/           # Ranker, AliasCanonicalizer
│   │   └── Rules/             # YAML rule engine
│   └── AppLocate.Cli/         # Console entry point
├── tests/
│   ├── AppLocate.Core.Tests/  # Unit tests for ranking, sources, rules
│   └── AppLocate.Cli.Tests/   # CLI integration and acceptance tests
├── rules/
│   └── apps.default.yaml      # Config/data path rules (147 apps)
├── docs/                      # Technical documentation
└── build/                     # Publish scripts
```

## Adding a New Discovery Source

Sources discover application artifacts by querying a specific location (registry, filesystem, package manager, etc.).

### 1. Create the Source

Create `src/AppLocate.Core/Sources/FooSource.cs`:

```csharp
using System.Runtime.CompilerServices;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources {
    public sealed class FooSource : ISource {
        public string Name => nameof(FooSource);

        public async IAsyncEnumerable<AppHit> QueryAsync(
            string query,
            SourceOptions options,
            [EnumeratorCancellation] CancellationToken ct) {
            
            await Task.Yield(); // Allow cancellation check
            
            // Skip if scope doesn't match
            if (options.UserOnly && /* this is machine scope */) {
                yield break;
            }

            // Normalize query for matching
            var normalizedQuery = query.ToLowerInvariant();
            
            // Discover and yield hits
            foreach (var item in DiscoverItems()) {
                if (ct.IsCancellationRequested) {
                    yield break;
                }

                if (!MatchesQuery(item, normalizedQuery)) {
                    continue;
                }

                // Build evidence if requested
                Dictionary<string, string>? evidence = null;
                if (options.IncludeEvidence) {
                    evidence = new Dictionary<string, string> {
                        ["FooKey"] = item.SomeValue
                    };
                }

                yield return new AppHit(
                    Type: HitType.Exe,           // or InstallDir, Config, Data
                    Scope: Scope.Machine,        // or Scope.User
                    Path: PathUtils.NormalizePath(item.Path) ?? item.Path,
                    Version: item.Version,       // null if unknown
                    PackageType: PackageType.Unknown,
                    Source: [Name],
                    Confidence: 0f,              // Set to 0; Ranker will score
                    Evidence: evidence
                );
            }
        }
    }
}
```

### 2. Register the Source

In `src/AppLocate.Core/Sources/SourceRegistryBuilder.cs`, add to the default builder:

```csharp
public static SourceRegistryBuilder CreateDefault() {
    return new SourceRegistryBuilder()
        // ... existing sources ...
        .Add(new FooSource());
}
```

Also update `src/AppLocate.Cli/Program.cs` in `BuildRegistry()`:

```csharp
.Add(new FooSource())
```

### 3. Add Tests

Create `tests/AppLocate.Core.Tests/FooSourceTests.cs`:

```csharp
public class FooSourceTests {
    [Fact]
    public async Task FooSource_ReturnsExpectedHits() {
        var source = new FooSource();
        var options = new SourceOptions(false, false, TimeSpan.FromSeconds(5), false, true, null);
        
        var hits = new List<AppHit>();
        await foreach (var hit in source.QueryAsync("test", options, CancellationToken.None)) {
            hits.Add(hit);
        }
        
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal(nameof(FooSource), h.Source[0]));
    }
}
```

## Source Implementation Guidelines

### DO

| Guideline | Reason |
|-----------|--------|
| Use `async IAsyncEnumerable` and yield results as discovered | Streaming allows early results |
| Respect `options.UserOnly` / `options.MachineOnly` | Skip irrelevant scopes early |
| Check `ct.IsCancellationRequested` in loops | Responsive cancellation |
| Swallow per-item exceptions | One bad entry shouldn't fail the whole source |
| Set `Confidence: 0f` | The Ranker handles scoring |
| Use `PathUtils.NormalizePath()` for paths | Consistent path format |
| Include evidence conditionally (`options.IncludeEvidence`) | Performance optimization |

### DON'T

| Guideline | Reason |
|-----------|--------|
| Throw exceptions from `QueryAsync` | Breaks the entire discovery pipeline |
| Make network calls | AppLocate is local-only |
| Execute discovered binaries | Security risk |
| Use global static state | Breaks testability |
| Access `WindowsApps` directly | Requires elevation |

## Testing with Fake Providers

For deterministic tests, sources should support fake providers via environment variables:

```csharp
// In your source
public FooSource() {
    var fakeJson = Environment.GetEnvironmentVariable("APPLOCATE_FOO_FAKE");
    _provider = string.IsNullOrEmpty(fakeJson)
        ? new FileSystemFooProvider()
        : new FakeFooProvider(fakeJson);
}
```

See `ScoopSource`, `ChocolateySource`, `MsixStoreSource` for examples.

## Adding App Rules

The YAML rule pack (`rules/apps.default.yaml`) defines config/data paths for known applications:

```yaml
- name: myapp
  match:
    - myapp
    - my-app
  config:
    - "%APPDATA%/MyApp/config.json"
  data:
    - "%LOCALAPPDATA%/MyApp/Data"
```

Rules are matched against query tokens and discovered filenames. See [apps.schema.md](../rules/apps.schema.md) for the full schema.

## Code Style

- Run `dotnet format` before committing (CI enforces this)
- Use file-scoped namespaces
- Prefer pattern matching and collection expressions
- Add XML doc comments to public APIs

## Commit Messages

Use conventional commit prefixes:

| Prefix | Use For |
|--------|---------|
| `feat:` | New features |
| `fix:` | Bug fixes |
| `docs:` | Documentation changes |
| `test:` | Adding/updating tests |
| `refactor:` | Code restructuring |
| `style:` | Formatting changes |
| `chore:` | Build/tooling changes |

Example: `fix: --install-dir flag now returns results when used alone`

## Pull Request Checklist

- [ ] Tests pass: `dotnet test`
- [ ] Format check passes: `dotnet format --verify-no-changes`
- [ ] New sources have unit tests
- [ ] CLI output changes have snapshot tests updated
- [ ] `AppHit` schema changes are additive only (backward compatibility)
- [ ] No network calls or binary execution
- [ ] Documentation updated if adding user-facing features

## Running Specific Tests

```bash
# All tests
dotnet test

# Specific test class
dotnet test --filter "FullyQualifiedName~RankingTests"

# Specific test method
dotnet test --filter "FullyQualifiedName~InstallDirFilter_OnlyInstallDirHits"

# CLI acceptance tests only
dotnet test tests/AppLocate.Cli.Tests --filter "FullyQualifiedName~Acceptance"
```

## Snapshot Tests

Snapshot tests verify CLI output stability. Located in `tests/AppLocate.Cli.Tests/Snapshots/`.

When output intentionally changes:
1. Run tests (they will fail)
2. Review the `*.received.*` files
3. If correct, replace the `*.verified.*` files
4. Commit with explanation

## Building for Release

```bash
# Full release build (x64 + ARM64 + PowerShell module)
pwsh ./build/publish.ps1 -X64 -Arm64 -Configuration Release

# Artifacts go to ./artifacts/
```

## Questions?

- Check existing [issues](https://github.com/aalex954/applocate/issues)
- Review [README.md](../README.md) for usage
- See [ranking-algorithm.md](ranking-algorithm.md) for scoring details
- See [cli-reference.md](cli-reference.md) for CLI flags
