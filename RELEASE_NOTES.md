# Release Notes - v0.1.6

## Highlights

- **PowerShell Gallery Support** — Install via `Install-Module -Name AppLocate`
- **CI/CD Improvements** — Selective job control and clearer workflow naming
- **Documentation** — New dataflow diagram and enhanced README

---

## New Features

### PowerShell Gallery Publishing
- Added `Install-Module -Name AppLocate -Scope CurrentUser` support
- Module bundles `applocate.exe` with PowerShell-friendly wrapper functions:
  - `Find-App` — Quick search returning parsed objects
  - `Get-AppLocateJson` — JSON output with filtering options
  - `Invoke-AppLocate` — Raw CLI invocation with any flags
  - `Set-AppLocatePath` / `Get-AppLocatePath` — Custom exe path configuration
- Added `build/publish-psgallery.ps1` script for local and CI publishing
- Module auto-discovers exe from: module directory → artifacts → system PATH

### CI Workflow Improvements
- Added `skip_winget` and `skip_psgallery` inputs for manual workflow runs
- Added manual approval gate for WinGet publishing (environment protection)
- Renamed jobs for clarity:
  - `publish` → `package` (Package Artifacts)
  - `release` → Create GitHub Release
  - `winget` → Publish to WinGet
  - `psgallery` → Publish to PowerShell Gallery

---

## Bug Fixes

- **WinGet portable packages**: Fixed executable discovery in portable WinGet packages
- **Scope inference**: Fixed User scope inference from `LOCALAPPDATA`/`APPDATA` environment variables
- **Tests**: Fixed VscodeScenario test to preserve system PATH in CI environments

---

## Documentation

- Added [Dataflow Diagram](docs/dataflow-diagram.md) explaining architecture and data flow
- Enhanced README with PowerShell Gallery installation instructions
- Updated copilot-instructions with version update guidance and code formatting rules

---

## Other Changes

- Added MIT LICENSE file
- Added `applocate.png` logo asset for PSGallery
- Removed WinGet Icons injection (requires verified publisher status)
- Converted test files to block-scoped namespaces (style)

---

## Installation

### WinGet
```powershell
winget install AppLocate.AppLocate
```

### PowerShell Gallery
```powershell
Install-Module -Name AppLocate -Scope CurrentUser
```

### Manual Download
Download from [GitHub Releases](https://github.com/aalex954/applocate/releases/tag/v0.1.6)

---

## Full Changelog

**22 commits** since v0.1.5

See [compare view](https://github.com/aalex954/applocate/compare/v0.1.5...v0.1.6) for complete diff.
