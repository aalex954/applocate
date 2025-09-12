# Requirements: Windows 11 CLI “App Locator”

## Purpose

Given a search string, locate an application’s installation directory, primary executable(s), and configuration/data locations. Return paths with evidence and confidence.

## Scope

* User- and machine-scope apps.
* MSI, MSIX/Store, EXE installers, ClickOnce/Squirrel/Electron, portable apps.
* Output in human-readable or structured formats.

## Platforms

* Windows 11, x64/ARM64.
* Works without admin by default. Supports optional elevation.

## CLI

```
applocate <query>
  [--exe] [--install-dir] [--config] [--data]
  [--all] [--user] [--machine]
  [--json | --csv | --text]
  [--limit N] [--confidence-min 0.6]
  [--fuzzy | --strict]
  [--running | --pid <n>]
  [--package-source]           # show MSI/MSIX/Store/EXE/Scoop/Choco/Winget
  [--refresh-index] [--index-path <dir>]
  [--threads N] [--timeout <sec>]
  [--verbose] [--trace] [--evidence]
  [--no-elevate | --elevate]
```

## Inputs

* `<query>`: app name, alias, or partial (e.g., `visual studio code`, `vscode`, `code`).

## Outputs

* Records with fields:

  * `type` ∈ {`install_dir`,`exe`,`config`,`data`}
  * `scope` ∈ {`user`,`machine`}
  * `path`
  * `version` (if available)
  * `package_type` ∈ {`MSI`,`MSIX`,`Store`,`EXE`,`Portable`,`ClickOnce`,`Squirrel`,`Scoop`,`Chocolatey`,`Winget`}
  * `source` (which data source produced it)
  * `confidence` (0–1)
  * `evidence` (keys/file hits used for scoring; on `--evidence`)
* Formats: `--text` (default), `--json`, `--csv`.

### Example (`--json`)

```json
[
  {
    "type":"exe",
    "scope":"user",
    "path":"C:\\Users\\u\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe",
    "version":"1.93.1",
    "package_type":"Squirrel",
    "source":["StartMenu","UninstallHKCU","FileSystem"],
    "confidence":0.94
  },
  {
    "type":"config",
    "scope":"user",
    "path":"C:\\Users\\u\\AppData\\Roaming\\Code\\User\\settings.json",
    "package_type":"Squirrel",
    "source":["KnownFolders","Heuristic"],
    "confidence":0.88
  }
]
```

## Data Sources (queried in parallel)

1. **Running processes**: `Get-Process` paths; `--running` or `--pid`.
2. **App Paths**: `HKLM/HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\*.exe`.
3. **Uninstall keys**:

   * `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\*`
   * `HKLM\Software\WOW6432Node\...\Uninstall\*`
   * `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\*`
     Fields: `DisplayName`, `InstallLocation`, `DisplayIcon`, `Publisher`, `Version`.
4. **Start Menu shortcuts**:
   `%ProgramData%\Microsoft\Windows\Start Menu\Programs\**\*.lnk`
   `%AppData%\Microsoft\Windows\Start Menu\Programs\**\*.lnk`.
5. **PATH resolution**: `where.exe`, PATH dirs, file existence.
6. **MSIX/Store**: `Get-AppxPackage` for current user; `InstallLocation`, `PackageFamilyName`.
7. **WindowsApps**: enumerate via package metadata; avoid direct file traversal unless elevated.
8. **Services & Scheduled Tasks**: image paths referencing app dirs.
9. **Package managers**: optional adapters that query installed lists and manifests:

   * Winget, Chocolatey, Scoop, npm/pnpm/yarn global, pipx.
10. **File system heuristics**:

    * `%LOCALAPPDATA%\Programs\**\*.exe`
    * `%LOCALAPPDATA%\*` and `%APPDATA%\*` known vendor trees
    * `C:\Program Files*\<Vendor>\<App>\*.exe`
    * `%PROGRAMDATA%\*` for machine-wide configs.

## Config/Data Heuristics

* Common per-app config roots:

  * `%APPDATA%\<App|Vendor>`
  * `%LOCALAPPDATA%\<App|Vendor>`
  * `%PROGRAMDATA%\<Vendor>\<App>`
  * For MSIX: `%LOCALAPPDATA%\Packages\<PFN>\LocalState|RoamingState`
* App-specific rules shipped as patterns:

  * e.g., VS Code `Roaming\Code\User\settings.json`
  * Chrome `Local\Google\Chrome\User Data`
* Rule pack is extensible via YAML.

## Matching & Ranking

* Normalize query: lowercase, strip punctuation, collapse spaces.
* Alias dictionary (e.g., `vscode→code`, `office→winword/excel`).
* Fuzzy match (token set ratio) across `DisplayName`, `Shortcut name`, `Exe name`, `InstallLocation`.
* Score components:

  * Exact exe name match +0.5
  * Alias match +0.2
  * Shortcut target exists +0.2
  * Uninstall `InstallLocation` exists +0.2
  * Recent process path +0.1
  * Penalties: broken link, missing path, multiple vendors.
* Final `confidence` ∈ \[0,1]; filter via `--confidence-min`.

## Behavior

* Default returns best per `type`. `--all` returns every hit.
* De-duplicate identical paths across sources.
* Expand environment variables and resolve `.lnk` targets.
* 32/64-bit awareness; prefer 64-bit when both present.
* No network access. Purely local.

## Indexing

* Optional on-disk index to speed repeated searches:

  * Stored at `%LOCALAPPDATA%\AppLocator\index.db` by default.
  * Built via `--refresh-index`. Includes hashes of LNK targets, registry snapshots, package lists.
  * Auto-invalidates on registry/package change timestamps.

## Security

* Least privilege by default. `--elevate` only when needed to read protected locations.
* Never executes target binaries.
* Sanitizes output to avoid control chars.
* No telemetry. Opt-in debug dumps only.

## Performance

* Parallel source queries; bounded by `--threads`.
* Timeouts per source; fail soft with partial results and per-source errors in `--verbose`.

## Logging

* Quiet by default. `--verbose` shows sources queried and counts.
* `--trace` shows query timings and failed sources.

## Exit Codes

* `0`: results found.
* `1`: no matches.
* `2`: bad arguments.
* `3`: permission denied for required source.
* `4`: internal error.

## Extensibility

* Plugin model for:

  * New package managers.
  * App-specific config rules.
  * Org alias packs.
* Plugins are data-only (YAML/JSON). No executable plugins.

## Testing: Acceptance Criteria

* Query “vscode” returns an `exe` and `config` for a standard per-user install with `confidence ≥ 0.8`.
* Query “Google Chrome” returns machine-scope `exe` in `Program Files` when present.
* Portable app in `D:\Tools\Foo\Foo.exe` with Start Menu shortcut is found and returns `install_dir` and `exe`.
* MSIX app returns `InstallLocation` and `Packages\<PFN>` data root.
* `--running` while Notepad running returns that process path.
* `--json` is stable and schema-valid.
* `--strict` excludes fuzzy alias matches.
* `--user` vs `--machine` filters results correctly.

## Non-Functional

* Cold search median < 1.0s on typical systems. Warm (indexed) < 200ms.
* Memory footprint idle < 100 MB.
* Works offline. No admin required for core features.

## Deliverables

* Single static EXE (`applocate.exe`) and optional PowerShell module (`AppLocate.psm1`) exposing the same functions.
* Man page/`--help`.
* YAML rule pack with 50+ popular apps.

### DO / AVOID
DO: incremental PRs per source / feature; keep `AppHit` stable; include evidence conditionally.
DO: add XML docs for public contract types.
AVOID: new P/Invoke signatures if an existing package solves it; global static state; altering JSON ordering.

### Open TODOs
- Decide on final `System.CommandLine` version & refactor CLI.
- Implement DI/aggregation layer (consider simple registrar for sources).
- Flesh out ranking with pooling and span-based tokenization.
- Add YAML rules + tests.

Provide feedback if any area needs deeper guidance (e.g., ranking algorithm detail, registry abstraction design).