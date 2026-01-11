# Ranking Algorithm

> **Accurate as of:** AppLocate v0.1.6

This document describes how AppLocate scores and ranks discovered application hits.

## Overview

AppLocate uses a multi-signal heuristic scoring algorithm that combines:

1. **Token matching** — How well the query tokens match the filename/path
2. **Evidence signals** — Provenance data from discovery sources
3. **Multi-source corroboration** — Hits found by multiple sources are boosted
4. **Type weighting** — Different hit types have different baselines
5. **Penalties** — Noise reduction for temporary/cache/auxiliary files

All scores are clamped to `[0, 1]`. Higher is better.

## Score Components

Use `--score-breakdown` to see how each result's score was computed:

```
[0.86] Exe C:\Program Files\Code\Code.exe
    breakdown: base=0.08 name=0.35 token=0.27 alias=0 evidence=0.10 multi=0.17 penalties=0 total=0.86
```

### Component Buckets

| Bucket | Components | Max Contribution |
|--------|------------|------------------|
| `base` | Type baseline | +0.03 to +0.08 |
| `name` | Filename exact/partial, collapsed substring, fuzzy Levenshtein, exact match bonus | ~+0.52 |
| `token` | Token coverage, partial token Jaccard, contiguous span | ~+0.47 |
| `alias` | Alias equivalence, directory alias | ~+0.40 |
| `evidence` | Evidence boosts, synergy, penalties | ~+0.47 or −0.15 |
| `multi` | Multi-source corroboration | +0.18 max |
| `penalties` | Path quality, noise, uninstall, cache artifacts | varies (negative) |

## Detailed Scoring Logic

### 1. Token Coverage (+0.25 max)

The query is tokenized by splitting on spaces, dashes, underscores, and dots. Tokens are compared against the filename and parent directory name.

```
Query: "visual studio code"
Tokens: ["visual", "studio", "code"]
Filename: "Code.exe" → tokens: ["code"]

Coverage = matched / query_tokens = 1/3 = 0.33
Score contribution = 0.33 × 0.25 = 0.083
```

**Token expansion**: CamelCase and numeric boundaries are expanded. `GoogleChrome` → `["google", "chrome"]`.

### 1b. Partial Token Jaccard (+0.08 max)

For partial matches, Jaccard similarity over the token union is computed:

```
jaccard = intersection / union
boost = jaccard × 0.08 × noise_factor
```

Noise factor reduces boost when candidate has many extra tokens:
- 2+ extra tokens: ×0.6
- 4+ extra tokens: ×0.4

### 2. Filename Match

| Condition | Boost |
|-----------|-------|
| Exact filename match (case-insensitive) | +0.30 |
| Alias equivalent (e.g., `code` ↔ `vscode`) | +0.22 |
| Partial substring match | +0.12 |
| Collapsed substring (ignoring spaces) | +0.08–0.15 |

### 3. Fuzzy Levenshtein (+0.06 max)

When there's no exact match, a normalized Levenshtein distance is computed:

```
similarity = 1 - (edit_distance / max_length)
```

If similarity > 0.5 and token coverage < 1:
```
boost = (similarity - 0.5) × 0.12
```

### 4. Contiguous Span (+0.14)

If all query tokens appear contiguously in the filename (ignoring separators), a bonus is applied:

```
Query: "google chrome"
Filename: "GoogleChrome.exe" → contiguous match → +0.14
Filename: "Chrome-Google-Helper.exe" → not contiguous → no bonus
```

### 5. Evidence Boosts

| Evidence Key | Boost |
|--------------|-------|
| `Shortcut` (Start Menu .lnk) | +0.10 |
| `ProcessId` (running process) | +0.08 |
| `Shortcut` + `ProcessId` synergy | +0.05 additional |
| `where` (found via where.exe) | +0.05 |
| `DirMatch` | +0.06 |
| `ExeName` | +0.04 |
| `AliasMatched` | +0.14 |
| `BrokenShortcut` | −0.15 |

### 6. Multi-Source Diminishing Returns (+0.18 max)

Hits discovered by multiple sources are boosted using harmonic series scaling:

```
boost = H(n) / 0.9 × 0.18

where H(n) = 1/2 + 1/3 + ... + 1/n (for n sources)
```

| Sources | Approximate Boost |
|---------|-------------------|
| 2 | +0.10 |
| 3 | +0.17 |
| 4+ | +0.18 (capped) |

### 7. Type Baseline

| Hit Type | Baseline |
|----------|----------|
| Exe | +0.08 |
| Config | +0.05 |
| InstallDir | +0.04 |
| Data | +0.03 |

### 8. Exact Match Bonus (+0.05)

When token coverage is 100% AND filename exactly matches query:

```
Query: "code"
Filename: "code.exe" → exact match with full coverage → +0.05 bonus
```

### 9. Directory Alias (+0.20 max)

For Config/Data hits, the parent directory name is also checked:

| Condition | Boost |
|-----------|-------|
| Exact directory name match | +0.20 |
| Alias equivalent directory | +0.18 |

```
Query: "vscode"
Path: "C:\Users\me\AppData\Roaming\Code\settings.json"
Directory: "Code" ↔ "vscode" → alias match → +0.18
```

## Penalties

### Path Quality Penalties

| Pattern | Penalty |
|---------|---------|
| `\temp\`, `/temp/`, `%temp%` | −0.18 |
| `\installer\`, `.tmp.exe` | −0.10 |
| `edgeupdate\temp` | −0.06 |
| `\temp\winget\` | −0.15 |

### Noise Penalties

| Condition | Penalty |
|-----------|---------|
| Extra unmatched tokens (>2) with contiguous span | −0.01 |
| Extra unmatched tokens (>1) without contiguous span | −0.02 per token (max −0.12) |
| Global noise (≥4 extra tokens, coverage < 1) | −0.01 per token (max −0.06) |
| System32 generic match (low coverage) | −0.22 |
| VSCode extension helper (not Code.exe) | −0.18 |
| Temp installer copies | −0.08 |

### Uninstaller/Updater Penalty (−0.25)

Executables matching these patterns are heavily penalized:
- `unins*`, `uninstall`, `unins000`
- `update-cache`, `setup.exe`

Unless the query explicitly contains "uninstall".

### Cache Artifact Penalties

| Pattern | Penalty |
|---------|---------|
| `code cache`, `videodecodestats` | −0.25 |
| `\update-cache\` | −0.22 |
| `\temp\winget\` | −0.10 |

### Application-Specific Penalties

**Steam auxiliary files** (−0.18): When query is `steam`, these are demoted:
- `webhelper`, `errorreporter`, `service`, `xboxutil`, `sysinfo`, `steamservice`

**FL Cloud Plugins** (−0.35): Cross-app noise suppression unless query contains `fl`, `cloud`, or `plugin`.

## Alias Dictionary

Built-in aliases enable equivalent matching:

| Query | Equivalents |
|-------|-------------|
| `vscode` | `code`, `visual studio code` |
| `chrome` | `google chrome` |
| `edge` | `microsoft edge` |
| `notepad++` | `notepadpp`, `npp` |
| `powershell` | `pwsh` |
| `oh-my-posh` | `ohmyposh`, `jandedobbeleer.ohmyposh` |
| `wt` | `windows terminal`, `wt.exe` |

Aliases work bidirectionally—querying `code` matches `vscode` and vice versa.

## Post-Scoring Adjustments

After initial scoring, the CLI applies additional adjustments:

### Install Directory Pairing Boost (+0.12 max)

InstallDir hits that contain a high-confidence Exe receive a boost:

```
boost = min(0.12, exe_confidence × 0.15)
```

### Generic Directory Penalty (−0.30)

Directories like `System32`, `bin` are demoted when a higher-confidence Exe exists in the same location.

### Confidence Floor (+0.08)

InstallDir hits paired with Exe ≥ 0.5 confidence receive a minimum floor of 0.08 to avoid showing 0.00.

## Collapse Behavior

Without `--all`, results are intelligently collapsed:

1. **Exe**: Up to 3 high-confidence executables from distinct directories
2. **InstallDir**: Paired with selected Exe hits, plus variant siblings (e.g., multiple versions)
3. **Config/Data**: Single best hit per type

Use `--all` to see every distinct hit.
