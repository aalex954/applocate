<h1>applocate</h1>

[![CodeQL](https://github.com/aalex954/applocate/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/aalex954/applocate/actions/workflows/github-code-scanning/codeql)
[![build-test-release](https://github.com/aalex954/applocate/actions/workflows/build-release.yml/badge.svg)](https://github.com/aalex954/applocate/actions/workflows/build-release.yml)
[![WinGet Package Version](https://img.shields.io/winget/v/AppLocate.AppLocate?label=Winget%20AppLocate&color=123456)](https://github.com/aalex954/applocate/releases/latest)
[![PowerShell Gallery Version](https://img.shields.io/powershellgallery/v/AppLocate?logo=powershell&label=PowerShell%20Gallery%20-%20AppLocate&color=123456)](https://www.powershellgallery.com/packages/AppLocate/0.1.6)

<table>
<tr>
<td width="96" valign="middle"><img src="assets/logo.svg" width="96" alt="applocate logo"/></td>
<td>

**Find any app on Windows — instantly.**

Ever needed to locate where an application is actually installed? Or find its config files for backup, migration, or troubleshooting? `AppLocate` searches registry keys, Start Menu shortcuts, running processes, package managers, and more. It then ranks results by confidence and returns structured output you can script against.

_Inspired by Linux's locate—but purpose-built for Windows application discovery._

</td>
</tr>
</table>

<p align="center">
<strong>No admin</strong> required. <strong>No network</strong> calls. <strong>No executing</strong> discovered binaries. Just fast, local discovery with deterministic JSON/CSV/text output.
</p>

<p align="center">
  <img src="https://github.com/user-attachments/assets/ac2d08f3-df46-42b1-a4f2-b77a359a0250" alt="applocate"/>
</p>

## Features

Unlike single-purpose tools that check one location, AppLocate casts a wide net—querying the registry, Start Menu shortcuts, running processes, PATH directories, Windows services, MSIX packages, and popular package managers like Scoop, Chocolatey, and WinGet—all in parallel. Results from every source are merged, deduplicated, and ranked by confidence so you get a single authoritative answer instead of hunting through scattered system locations. Need to find where that mystery `node.exe` is actually running from? AppLocate sees it live and traces it back to its install root.

Here's what's currently implemented:

| Area | Status | Notes |
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

## How It Works

AppLocate runs all discovery sources in parallel, streams results through a ranking pipeline, and collapses to the best hits per type. For a detailed architecture walkthrough—including source APIs, scoring components, and output flow—see the [Dataflow Diagram](docs/dataflow-diagram.md).

## Installation

Pre-built binaries are available for Windows x64 and ARM64. Choose your preferred method:

### WinGet (Windows Package Manager)
```pwsh
winget install AppLocate.AppLocate
```

Stable releases are automatically submitted to the [Windows Package Manager Community Repository](https://github.com/microsoft/winget-pkgs). Pre-release versions (alpha, beta, rc) are not published to WinGet.

### PowerShell Gallery
```pwsh
Install-Module -Name AppLocate -Scope CurrentUser
```

The module bundles `applocate.exe` and exposes PowerShell-friendly functions:
```pwsh
# Search and get parsed objects
Find-App "chrome" | Select-Object path, confidence

# Get JSON output with filtering
Get-AppLocateJson -Query "vscode" -ConfidenceMin 0.7 -Limit 3

# Raw CLI invocation with any flags
Invoke-AppLocate "git" "--all" "--evidence" -Json
```

### Manual Download
Download the latest release from [GitHub Releases](https://github.com/aalex954/applocate/releases):
- `applocate-win-x64.zip` – Windows x64
- `applocate-win-arm64.zip` – Windows ARM64

Extract and add to your PATH, or run directly.

## Usage

```css
PS> applocate curl
[0.72] Exe C:\Program Files\Git\mingw64\bin\curl.exe
[0.69] Exe C:\Windows\System32\curl.exe
[0.62] Exe C:\Users\user\AppData\Local\...\curl-x.xx.x\bin\curl.exe
```

More examples:

```pwsh
# Find VS Code installation and config paths
applocate code

# Get JSON output for scripting (e.g., backup settings)
applocate "visual studio" --json --config

# Find where a running process lives
applocate node --running --exe

# Locate Git install directory for PATH debugging  
applocate git --install-dir --json | ConvertFrom-Json | Select-Object -Expand path

# Find all Chrome data (profiles, cache) with evidence of where it came from
applocate chrome --data --all --evidence
```

Options (implemented CLI surface):
```
Input:
  <query>                                       App name / alias / partial tokens
  --                                            Treat following tokens as literal query

Output Format:
  --json | --csv | --text                       Output format (default: text)
  --no-color                                    Disable ANSI color in text output

Filtering:
  --user | --machine                            Scope filters
  --exe | --install-dir | --config | --data     Type filters (combinable)
  --all                                         Return ALL hits (no per-type collapsing)
  --confidence-min <f>                          Minimum confidence threshold (0-1)
  --limit <N>                                   Max results after filtering

Sources:
  --running                                     Include running process enumeration
  --pid <n>                                     Target specific process id (implies --running)

Output Enrichment:
  --evidence                                    Include evidence dictionary
  --evidence-keys <k1,k2>                       Only specified evidence keys (implies --evidence)
  --score-breakdown                             Show scoring component contributions per result
  --package-source                              Show package type & source list in text/CSV

Performance:
  --threads <n>                                 Max parallel source queries (default: min(CPU,16))
  --timeout <sec>                               Per-source soft timeout (default: 5)

Diagnostics:
  --verbose                                     Verbose diagnostics (warnings)
  --trace                                       Per-source timing diagnostics (stderr)
  --help                                        Show help
```

### Default Behavior

Without `--all`, results are intelligently collapsed:

- **`exe`**: Up to 3 high-confidence executables from distinct directories, each paired with its install directory. Variant siblings (e.g., multiple installed versions) may also surface.
- **`config` / `data`**: Single best hit per type, tie-broken by scope (machine > user) then evidence richness.

Use `--all` to see every distinct hit (useful for debugging ranking or alternate install roots).

### Exit Codes

- **0**: Results found, or help displayed (when run without arguments or with `--help`)
- **1**: No matches found
- **2**: Argument/validation error

## Output and Evidence

### Minimal JSON Hit Example
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

### Confidence

`--evidence` adds key/value provenance. Examples:

```jsonc
{"Shortcut":"C:/Users/u/.../Code.lnk"}
{"ProcessId":"1234","ExeName":"Code.exe"}
{"DisplayName":"Google Chrome","HasInstallLocation":"true"}
```
Confidence heuristic (phase 1): token & fuzzy coverage, exact exe/dir boosts, alias equivalence, evidence synergy (shortcut+process), multi-source diminishing returns, penalties (temp/broken). Scores ∈ [0,1].

### Score Breakdown
Use `--score-breakdown` to see how each result's confidence score was computed:
```
[0.86] Exe C:\Users\user\AppData\Local\Programs\Microsoft VS Code\Code.exe
    breakdown: base=0.08 name=0.35 token=0.27 alias=0 evidence=0 multi=0.17 penalties=0 total=0.86
```

| Bucket | Description |
|--------|-------------|
| `base` | Type baseline (Exe starts higher than InstallDir) |
| `name` | Filename match quality (exact, partial, fuzzy Levenshtein) |
| `token` | Query token coverage + contiguous span bonus |
| `alias` | Built-in alias match (e.g., "code" → "vscode") |
| `evidence` | Registry/shortcut/process evidence boosts |
| `multi` | Multi-source corroboration (diminishing returns) |
| `penalties` | Path quality deductions (temp, cache, generic dirs) |

## Security & Privacy

* No network or telemetry
* Does not execute discovered binaries
* Least privilege by default (no admin required for core features)
* Read-only posture – only queries registry, file system, and package managers
* Output sanitization to prevent terminal injection

For enterprise deployments or security-conscious environments, see [SECURITY_REVIEW.md](SECURITY_REVIEW.md) for a detailed threat model, attack surface analysis, and hardening recommendations.

## Project Layout

```
src/AppLocate.Core       # Domain models, abstractions, sources, ranking & rules engine
  ├─ Abstractions/       # Interfaces (ISource, ISourceRegistry, IAmbientServices)
  ├─ Models/             # AppHit, ScoreBreakdown, PathUtils, EvidenceKeys
  ├─ Sources/            # All discovery sources (Registry, AppPaths, StartMenu, Process, PATH, MSIX, Services, HeuristicFS, Scoop, Chocolatey, Winget)
  ├─ Ranking/            # Scoring logic, alias canonicalization
  └─ Rules/              # YAML rule engine for config/data expansion
src/AppLocate.Cli        # CLI entry point with System.CommandLine + manual parsing
tests/AppLocate.Core.Tests   # Unit tests for ranking, rules, sources
tests/AppLocate.Cli.Tests    # CLI integration, acceptance, snapshot tests
rules/apps.default.yaml  # Config/data rule pack (147 apps)
build/publish.ps1        # Single-file publish script (win-x64 / win-arm64)
assets/                  # Logo (SVG, ICO)
AppLocate.psm1           # PowerShell module wrapper
```

## Build

### Quick Start

```pwsh
dotnet restore
dotnet build
dotnet format --verify-no-changes   # CI enforces formatting
dotnet test
```

Run (may produce limited or no hits depending on environment):
```pwsh
dotnet run --project src/AppLocate.Cli -- vscode --json
```
Exit codes: 0 (results or help), 1 (no matches), 2 (argument error). See [Usage](#usage) for details.

### Publish Single-File

```pwsh
pwsh ./build/publish.ps1 -X64 -Arm64 -Configuration Release
```
Artifacts land under `./artifacts/<rid>/`.

Each published RID artifact now includes a CycloneDX SBOM file (`sbom-<rid>.json`) listing dependency components for supply-chain transparency.


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
- Inject Scoop apps via `APPLOCATE_SCOOP_FAKE` (JSON object with roots/apps) for deterministic enumeration.
- Inject Chocolatey packages via `APPLOCATE_CHOCO_FAKE` (JSON object with directories/metadata) for deterministic enumeration.
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

## Advanced 

### Environment Overrides
For deterministic tests:
* `APPDATA`, `LOCALAPPDATA`, `PROGRAMDATA`
* `PATH`
* `APPLOCATE_MSIX_FAKE` (JSON array of fake MSIX packages)
* `APPLOCATE_SCOOP_FAKE` (JSON object with roots and apps)
* `APPLOCATE_CHOCO_FAKE` (JSON object with directories and package metadata)

Example:
```pwsh
$env:APPLOCATE_MSIX_FAKE='[{"name":"SampleApp","family":"Sample.App_123","install":"C:/tmp/sample","version":"1.0.0.0"}]'
applocate sample --json
```

## Roadmap

Backlog / Later:
- [ ] Code signing for releases
- [ ] Elevation strategy (`--elevate` / `--no-elevate`) & privileged source gating

## Contributing
See `.github/copilot-instructions.md` for design/extension guidance. Keep `AppHit` schema backward compatible.
