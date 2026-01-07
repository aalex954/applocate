# AppLocate Security Review Report

**Date:** January 6, 2026  
**Version Reviewed:** Current (main branch)  
**Reviewer:** Security Analysis  
**Classification:** Internal Use Only

---

## Executive Summary

AppLocate is a Windows 11 CLI tool designed to locate application installations, executables, and configuration/data paths on the local system. The tool operates with a read-only posture, querying registry, file system, running processes, and package managers.

**Overall Security Posture: GOOD with minor recommendations**

The application follows many security best practices including:
- Least privilege operation (no admin required for core features)
- No remote network access
- No execution of discovered binaries
- Proper input validation and sanitization

However, several areas warrant attention to further harden the application.

---

## Table of Contents

1. [Threat Model](#1-threat-model)
2. [Critical Findings](#2-critical-findings)
3. [High Severity Findings](#3-high-severity-findings)
4. [Medium Severity Findings](#4-medium-severity-findings)
5. [Low Severity Findings](#5-low-severity-findings)
6. [Informational Findings](#6-informational-findings)
7. [Security Recommendations](#7-security-recommendations)
8. [Compliance Considerations](#8-compliance-considerations)

---

## 1. Threat Model

### 1.1 Assets
- Local file system paths and metadata
- Registry data (uninstall keys, app paths, services)
- Running process information
- Package manager installation data
- User configuration/data file locations

### 1.2 Threat Actors
- **Malicious Application on System**: Could exploit AppLocate output to discover installed security tools
- **Privileged User Misuse**: Using tool for reconnaissance
- **Supply Chain**: Compromised dependencies or build artifacts

### 1.3 Attack Surfaces
| Surface | Description | Risk Level |
|---------|-------------|------------|
| CLI Input | User-provided query string | Medium |
| File System | Directory/file enumeration | Low |
| Registry | Registry key enumeration | Low |
| External Processes | where.exe, winget, PowerShell | Medium |
| Rules YAML | External rule pack loading | Medium |
| JSON Output | Structured data output | Low |

---

## 2. Critical Findings

**None identified.**

The application maintains a read-only posture and does not execute discovered binaries, mitigating most critical attack vectors.

---

## 3. High Severity Findings

### 3.1 COM Object Instantiation for Shortcut Resolution

**Location:** [StartMenuShortcutSource.cs](src/AppLocate.Core/Sources/StartMenuShortcutSource.cs#L117-L130)

**Description:**  
The `ResolveShortcut` method uses dynamic COM interop with `WScript.Shell` to resolve shortcut targets:

```csharp
var shellType = Type.GetTypeFromProgID("WScript.Shell");
if (shellType == null) { return null; }
dynamic shell = Activator.CreateInstance(shellType)!;
dynamic sc = shell.CreateShortcut(lnk);
var target = sc.TargetPath as string;
```

**Risk:**  
- COM object hijacking if a malicious `WScript.Shell` is registered
- DLL injection via COM server misconfiguration
- Potential for unintended code execution through malformed .lnk files

**Impact:** High - Could lead to arbitrary code execution

**Recommendation:**
1. Consider using P/Invoke with `IShellLink` interface directly instead of WScript.Shell
2. Validate shortcut file integrity before processing
3. Add try-catch isolation to prevent COM exceptions from affecting application stability

---

### 3.2 External Process Execution Without Full Path Validation

**Location:** [WingetSource.cs](src/AppLocate.Core/Sources/WingetSource.cs#L28-L40), [PathSearchSource.cs](src/AppLocate.Core/Sources/PathSearchSource.cs#L65-L72)

**Description:**  
External processes are executed with partial path reliance:

```csharp
// WingetSource.cs
var psi = new ProcessStartInfo("winget", "--version") { ... };

// PathSearchSource.cs
p.StartInfo.FileName = Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\where.exe");
```

**Risk:**  
- `winget` relies on PATH resolution, susceptible to PATH hijacking
- Environment variable expansion could be manipulated (`%SystemRoot%`)

**Impact:** High - PATH/DLL hijacking could execute malicious code

**Recommendation:**
1. Use fully qualified paths for all external executables
2. Validate that resolved paths are within expected system directories
3. Consider using `CreateProcess` flags to prevent DLL search order hijacking

---

## 4. Medium Severity Findings

### 4.1 YAML Rule Pack File Loading Without Signature Verification

**Location:** [RulesEngine.cs](src/AppLocate.Core/Rules/RulesEngine.cs), [Program.cs](src/AppLocate.Cli/Program.cs#L480-L510)

**Description:**  
The rules engine loads YAML files from predictable locations without integrity verification:

```csharp
var rulesPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "rules", "apps.default.yaml");
if (!File.Exists(rulesPath)) {
    var alt = Path.Combine(Directory.GetCurrentDirectory(), "rules", "apps.default.yaml");
    rulesPath = File.Exists(alt) ? alt : string.Empty;
}
```

**Risk:**  
- Rule pack substitution attack from current working directory
- Path traversal via `..` segments (although normalized)
- Malicious config/data paths could be injected via crafted rule files

**Impact:** Medium - Could expose sensitive file paths or cause denial of service

**Recommendation:**
1. Load rules only from verified installation directory
2. Implement file hash/signature verification for rule packs
3. Remove current working directory fallback in production builds
4. Validate expanded paths don't escape expected directories

---

### 4.2 Scheduled Task XML File Content Parsing

**Location:** [ServicesTasksSource.cs](src/AppLocate.Core/Sources/ServicesTasksSource.cs#L129-L165)

**Description:**  
Scheduled task XML files are read and searched for executable paths using string operations:

```csharp
var content = File.ReadAllText(tf);
if (!content.Contains(".exe", StringComparison.OrdinalIgnoreCase)) { continue; }
```

**Risk:**  
- Large or malformed task files could cause performance issues
- XML injection patterns might not be properly handled
- Reading arbitrary files in `%SystemRoot%\System32\Tasks` could include malicious task definitions

**Impact:** Medium - Denial of service, information disclosure

**Recommendation:**
1. Implement file size limits before reading
2. Use proper XML parsing instead of string operations
3. Add timeout for file operations

---

### 4.3 PowerShell Execution for MSIX Package Enumeration

**Location:** [MsixStoreSource.cs](src/AppLocate.Core/Sources/MsixStoreSource.cs#L17-L45)

**Description:**  
PowerShell is invoked with `-ExecutionPolicy Bypass`:

```csharp
p.StartInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"$ErrorActionPreference='SilentlyContinue'; Get-AppxPackage | ForEach-Object { ... }\"";
```

**Risk:**  
- `-ExecutionPolicy Bypass` overrides system policy
- Complex PowerShell command with string interpolation
- Output parsing relies on pipe-delimited format

**Impact:** Medium - Policy bypass, potential for command injection in edge cases

**Recommendation:**
1. Use Windows Runtime APIs directly (Windows.Management.Deployment) instead of PowerShell
2. If PowerShell is required, use `-ExecutionPolicy Restricted` with signed scripts
3. Consider using `PackageManager` API from Microsoft.Windows.SDK.Contracts

---

### 4.4 Environment Variable Expansion in User-Controllable Paths

**Location:** [PathUtils.cs](src/AppLocate.Core/Models/PathUtils.cs#L11-L14), [RulesEngine.cs](src/AppLocate.Core/Rules/RulesEngine.cs)

**Description:**  
Environment variables are expanded without validation:

```csharp
var s = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('\"'));
```

**Risk:**  
- User-defined environment variables could redirect paths unexpectedly
- Environment variable substitution could expose unexpected locations

**Impact:** Medium - Path manipulation, information disclosure

**Recommendation:**
1. Whitelist allowed environment variables
2. Validate expanded paths are within expected root directories
3. Log unexpected environment variable expansions

---

## 5. Low Severity Findings

### 5.1 Process Memory Access Without Privilege Checks

**Location:** [ProcessSource.cs](src/AppLocate.Core/Sources/ProcessSource.cs#L31-L35)

**Description:**  
The code attempts to access `MainModule.FileName` for all processes:

```csharp
System.Diagnostics.Process[] procs;
try { procs = System.Diagnostics.Process.GetProcesses(); }
try { mainModulePath = p.MainModule?.FileName; } catch { }
```

**Risk:**  
- AccessDeniedException for protected processes is silently swallowed
- Could indicate privilege escalation attempts to attackers monitoring for access patterns

**Impact:** Low - Information leakage about access attempts

**Recommendation:**
1. Filter processes by session ID before attempting access
2. Log access denied scenarios in trace mode
3. Consider using WMI queries for safer process enumeration

---

### 5.2 Unbounded Directory Recursion Depth

**Location:** [StartMenuShortcutSource.cs](src/AppLocate.Core/Sources/StartMenuShortcutSource.cs#L67-L68)

**Description:**  
Directory enumeration uses `SearchOption.AllDirectories`:

```csharp
files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
```

**Risk:**  
- Symbolic link loops could cause infinite recursion
- Deep directory structures could cause stack overflow or long delays

**Impact:** Low - Denial of service

**Recommendation:**
1. Implement maximum depth limit
2. Track visited directories to prevent symlink loops
3. Add early timeout enforcement

---

### 5.3 Index File Stored in Predictable Location

**Location:** [IndexStore.cs](src/AppLocate.Core/Indexing/IndexStore.cs)

**Description:**  
The index cache is stored at `%LOCALAPPDATA%\AppLocator\index.db` without access controls.

**Risk:**  
- Other applications could read/modify the index
- Cache poisoning could influence future searches

**Impact:** Low - Index manipulation (feature currently removed but code exists)

**Recommendation:**
1. Set restrictive ACLs on the index directory
2. Validate index integrity on load
3. Consider encrypting sensitive path data

---

### 5.4 JSON Serialization Without Size Limits

**Location:** [Program.cs](src/AppLocate.Cli/Program.cs#L1447-L1449)

**Description:**  
JSON output is serialized without size constraints:

```csharp
var jsonOut = JsonSerializer.Serialize(filtered, AppLocateJsonContext.Default.IReadOnlyListAppHit);
Console.Out.WriteLine(jsonOut);
```

**Risk:**  
- Extremely large result sets could cause memory exhaustion
- Output to pipes could block indefinitely

**Impact:** Low - Denial of service

**Recommendation:**
1. Enforce maximum result count even without `--limit`
2. Implement streaming JSON output for large result sets
3. Add output size warnings in verbose mode

---

### 5.5 ANSI Escape Sequence Injection

**Location:** [Program.cs](src/AppLocate.Cli/Program.cs#L1407-L1430)

**Description:**  
ANSI escape sequences are included in output:

```csharp
private static class Ansi {
    public const string Reset = "\x1b[0m";
    public const string Green = "\x1b[32m";
    ...
}
```

**Risk:**  
- If file paths contain ANSI sequences, they could manipulate terminal output
- Could be used for terminal injection attacks if paths are user-controlled

**Impact:** Low - Terminal manipulation, log injection

**Recommendation:**
1. Sanitize all path strings before including in colored output
2. Strip control characters from paths
3. Add `--no-color` documentation emphasizing security use cases

---

## 6. Informational Findings

### 6.1 Broad Exception Swallowing

**Multiple Locations**

**Description:**  
Many try-catch blocks swallow all exceptions silently:

```csharp
catch { /* swallow persistence errors silently */ }
catch { return raw; }
catch { }
```

**Observation:**  
While this prevents crashes, it also hides potential security-relevant errors and makes debugging difficult.

**Recommendation:**
1. Log swallowed exceptions at trace level
2. Consider specific exception types
3. Add structured error reporting in verbose mode

---

### 6.2 Test Environment Variables for Mocking

**Location:** [MsixStoreSource.cs](src/AppLocate.Core/Sources/MsixStoreSource.cs#L46-L68)

**Description:**  
Environment variable `APPLOCATE_MSIX_FAKE` allows injecting fake package data:

```csharp
var json = Environment.GetEnvironmentVariable("APPLOCATE_MSIX_FAKE");
```

**Observation:**  
This is a testing facility but should be disabled in release builds.

**Recommendation:**
1. Wrap in `#if DEBUG` preprocessor directive
2. Or check for specific test flag at startup
3. Document that this is not a supported feature

---

### 6.3 Hardcoded Paths and Magic Strings

**Multiple Locations**

**Description:**  
Various hardcoded paths exist:
- `C:\ProgramData\chocolatey`
- `C:\ProgramData\scoop`
- Registry key paths

**Observation:**  
While these are well-known locations, they should be configurable for non-standard installations.

**Recommendation:**
1. Centralize path constants
2. Allow configuration override for enterprise environments
3. Document customization options

---

## 7. Security Recommendations

### 7.1 Immediate Actions (High Priority)

| # | Action | Effort | Impact |
|---|--------|--------|--------|
| 1 | Replace WScript.Shell COM with P/Invoke IShellLink | Medium | Eliminates COM hijacking |
| 2 | Use fully qualified paths for external executables | Low | Prevents PATH hijacking |
| 3 | Remove CWD fallback for rule loading | Low | Prevents rule substitution |

### 7.2 Short-Term Actions (Medium Priority)

| # | Action | Effort | Impact |
|---|--------|--------|--------|
| 4 | Replace PowerShell with Windows Runtime APIs | High | Removes execution policy bypass |
| 5 | Add file size limits for task XML parsing | Low | Prevents DoS |
| 6 | Implement environment variable whitelist | Medium | Limits path manipulation |
| 7 | Add depth limits to directory recursion | Low | Prevents symlink attacks |

### 7.3 Long-Term Actions (Low Priority)

| # | Action | Effort | Impact |
|---|--------|--------|--------|
| 8 | Add rule pack signature verification | High | Supply chain protection |
| 9 | Implement structured logging | Medium | Improves incident response |
| 10 | Add output size limits and streaming | Medium | Prevents resource exhaustion |
| 11 | Disable test mocking in release builds | Low | Reduces attack surface |

---

## 8. Compliance Considerations

### 8.1 Privacy
- Tool discovers and reports file paths which may reveal user behavior
- Consider adding `--redact-usernames` flag for enterprise deployments
- Document data handling in privacy policy

### 8.2 Audit Logging
- Consider optional audit logging for enterprise compliance
- Log queries and results to Windows Event Log

### 8.3 Access Control
- Tool currently has no authentication/authorization
- Enterprise deployments may need to restrict which users can query
- Consider integration with Windows security context

---

## Appendix A: Files Reviewed

| File | Lines | Security-Relevant Code |
|------|-------|----------------------|
| Program.cs | 1530 | CLI parsing, output, scope handling |
| ProcessSource.cs | ~90 | Process enumeration |
| RegistryUninstallSource.cs | ~200 | Registry access |
| StartMenuShortcutSource.cs | ~140 | COM interop, shortcut resolution |
| ServicesTasksSource.cs | ~270 | Service/task enumeration |
| MsixStoreSource.cs | ~240 | PowerShell execution |
| WingetSource.cs | ~360 | External process execution |
| ScoopSource.cs | ~310 | Package manager discovery |
| ChocolateySource.cs | ~250 | Package manager discovery |
| HeuristicFsSource.cs | ~165 | File system enumeration |
| PathSearchSource.cs | ~300 | where.exe execution |
| RulesEngine.cs | ~75 | YAML parsing |
| IndexStore.cs | ~230 | Cache persistence |
| PathUtils.cs | ~55 | Path normalization |
| AppLocate.psm1 | ~60 | PowerShell wrapper |

---

## Appendix B: Attack Scenarios

### Scenario 1: PATH Hijacking via winget
An attacker places a malicious `winget.exe` earlier in the PATH. When AppLocate calls `winget --version`, the malicious binary executes.

**Mitigation:** Use `C:\Program Files\WindowsApps\...\winget.exe` full path.

### Scenario 2: Rule Pack Substitution
An attacker creates a `rules/apps.default.yaml` in the current directory with malicious config paths pointing to sensitive files.

**Mitigation:** Remove CWD fallback; only load from installation directory.

### Scenario 3: Shortcut COM Hijacking
An attacker registers a malicious COM server for `WScript.Shell` ProgID. When AppLocate resolves shortcuts, malicious code executes.

**Mitigation:** Use `IShellLink` P/Invoke instead of COM automation.

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-01-06 | Security Analysis | Initial review |

---

*End of Security Review Report*
