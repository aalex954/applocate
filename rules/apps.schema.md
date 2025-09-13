# AppLocate Rules YAML Schema (Draft)

## Top-Level Structure
A rules file is a YAML sequence of rule objects. Each rule describes matching criteria and optional config/data path patterns.

```yaml
- match:
    anyOf: ["Visual Studio Code", "Code.exe", "vscode", "code"]
  config:
    - "%APPDATA%/Code/User/settings.json"
  data:
    - "%APPDATA%/Code/*"
  evidence:
    add:
      DirMatch: "true"
```

## Rule Object Fields
- `match` (required): object containing one or more of:
  - `anyOf` (string list): Case-insensitive substrings or exact tokens to match against candidate path, filename, or display names.
  - `allOf` (string list): All listed tokens/substrings must appear.
  - `regex` (string): Optional ECMAScript-compatible regex applied to normalized candidate path.
- `config` (string list, optional): File or directory glob patterns (forward slashes). Environment variables allowed (`%APPDATA%`). `*` and `**` supported.
- `data` (string list, optional): Same pattern semantics as `config` for broader data/profile roots.
- `evidence.add` (map, optional): Key/value pairs injected into hit evidence when the rule matches.
- `scope` (string optional): `user` | `machine` hint restricting pattern evaluation.
- `weight` (number optional): Additional score tweak (0-0.15) applied after base scoring to discriminate multiple rules for same app.

## Matching Semantics
1. Normalize candidate path to lowercase.
2. If `anyOf` present: at least one token substring must be present.
3. If `allOf` present: every token substring must be present.
4. If `regex` present: must match.
5. Rule passes if all provided criteria sections pass.

## Expansion
For each matched app hit, the engine will emit additional synthetic hits for each `config` and `data` pattern (if existing on disk) or record patterns for lazy existence checks (future). Evidence from `evidence.add` merges.

## Determinism
- Preserve rule order; evaluation is sequential and merge-stable.
- Avoid overlapping identical patterns unless a differing `weight` disambiguates.

## Future Considerations
- External plugin rule packs merged after core pack (override by name key once added).
- Conditional OS / architecture fields.
- Templated variables from detected install root (e.g., `${InstallDir}`) once resolver is implemented.
