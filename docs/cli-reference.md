# CLI Reference

> **Accurate as of:** AppLocate v0.1.6

Complete command-line reference for `applocate`.

## Synopsis

```
applocate <query> [options]
applocate -- <literal query with dashes>
```

## Arguments

| Argument | Description |
|----------|-------------|
| `<query>` | Application name, alias, or partial match (e.g., `vscode`, `chrome`, `visual studio`) |
| `--` | Sentinel: everything after is treated as literal query (useful for queries starting with `-`) |

## Output Format Options

| Flag | Description |
|------|-------------|
| `--json` | Output results as a JSON array |
| `--csv` | Output results as CSV with header row |
| `--text` | Force plain text output (default) |
| `--no-color` | Disable ANSI color codes in text output |

## Filtering Options

### Scope Filters

| Flag | Description |
|------|-------------|
| `--user` | Only return user-scope results (e.g., `%APPDATA%`, `%LOCALAPPDATA%`) |
| `--machine` | Only return machine-scope results (e.g., `Program Files`, `ProgramData`) |

### Type Filters

Type filters can be combined. If none specified, all types are returned.

| Flag | Description |
|------|-------------|
| `--exe` | Only executable hits (`HitType.Exe`) |
| `--install-dir` | Only installation directory hits (`HitType.InstallDir`) |
| `--config` | Only configuration file/directory hits (`HitType.Config`) |
| `--data` | Only data directory hits (`HitType.Data`) |

### Result Control

| Flag | Description |
|------|-------------|
| `--all` | Return all hits; disable per-type collapsing (default returns best per type) |
| `--limit <n>` | Maximum number of results to return |
| `--confidence-min <f>` | Minimum confidence threshold (0.0–1.0) |

## Process Discovery

| Flag | Description |
|------|-------------|
| `--running` | Include running process executables (enables `ProcessSource`) |
| `--pid <n>` | Target a specific process ID; adds its executable regardless of name match (implies `--running`) |

## Output Enrichment

| Flag | Description |
|------|-------------|
| `--evidence` | Include evidence dictionary showing discovery provenance |
| `--evidence-keys <k1,k2>` | Only include specified evidence keys (comma-separated; implies `--evidence`) |
| `--score-breakdown` | Show scoring component contributions per result |
| `--package-source` | Show package type and source list in text/CSV output |

## Performance Options

| Flag | Description |
|------|-------------|
| `--threads <n>` | Maximum parallel source queries (default: logical CPU count, capped at 16) |
| `--timeout <sec>` | Per-source timeout in seconds (default: 5, max: 300) |

## Diagnostics

| Flag | Description |
|------|-------------|
| `--verbose` | Emit diagnostic warnings to stderr |
| `--trace` | Emit per-source timing diagnostics to stderr |
| `--help`, `-h` | Show help and exit |
| `--version`, `-V` | Show version and exit |

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Results found, or `--help`/`--version` shown |
| `1` | No matches found |
| `2` | Argument or validation error |

## Examples

### Basic Usage

```bash
# Find VS Code installation
applocate code

# Find all Chrome-related paths
applocate chrome --all

# Get JSON output for scripting
applocate "visual studio" --json
```

### Filtering

```bash
# Only executables
applocate git --exe

# Only installation directories
applocate steam --install-dir

# Only user-scope config files
applocate vscode --config --user

# Combine filters: exe and install-dir with high confidence
applocate node --exe --install-dir --confidence-min 0.7
```

### Process Discovery

```bash
# Find where a running process lives
applocate node --running

# Get details for a specific PID
applocate mystery --pid 1234 --evidence
```

### Scripting

```bash
# Get install path as variable (PowerShell)
$path = (applocate git --json --exe --limit 1 | ConvertFrom-Json)[0].path

# Export all Python installations to CSV
applocate python --csv --all > python-installs.csv

# Backup VS Code settings
$configDir = (applocate code --json --config --limit 1 | ConvertFrom-Json)[0].path
Copy-Item $configDir -Destination ./backup -Recurse
```

### Debugging

```bash
# See scoring breakdown
applocate steam --score-breakdown

# View discovery provenance
applocate chrome --evidence

# Trace source timing
applocate obs --trace 2>&1
```

## Output Schema

### JSON Output

Each result is an object with:

| Field | Type | Description |
|-------|------|-------------|
| `type` | `int` | Hit type: `0`=InstallDir, `1`=Exe, `2`=Config, `3`=Data |
| `scope` | `int` | Scope: `0`=User, `1`=Machine |
| `path` | `string` | Absolute path to the artifact |
| `version` | `string?` | Version if known (from registry/manifest) |
| `packageType` | `int` | Package type enum (see below) |
| `source` | `string[]` | Discovery sources that found this hit |
| `confidence` | `float` | Confidence score (0.0–1.0) |
| `evidence` | `object?` | Key-value provenance data (if `--evidence`) |
| `breakdown` | `object?` | Score component breakdown (if `--score-breakdown`) |

### Package Types

| Value | Name | Description |
|-------|------|-------------|
| `0` | MSI | Windows Installer package |
| `1` | MSIX | Modern packaged app |
| `2` | Store | Microsoft Store app |
| `3` | EXE | Traditional installer or unknown |
| `4` | Portable | Xcopy/portable distribution |
| `5` | ClickOnce | ClickOnce deployment |
| `6` | Squirrel | Squirrel framework app |
| `7` | Scoop | Scoop package manager |
| `8` | Chocolatey | Chocolatey package |
| `9` | Winget | Windows Package Manager |
| `10` | Unknown | Unclassified |

### CSV Output

Header row followed by comma-separated values. Fields match JSON schema (excluding nested objects).

### Text Output

```
[confidence] Type Path
```

With `--score-breakdown`:
```
[confidence] Type Path
    breakdown: base=X name=X token=X alias=X evidence=X multi=X penalties=X total=X
```
