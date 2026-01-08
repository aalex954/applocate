# AppLocate Dataflow Diagram

This document describes the data flow through the AppLocate application from user input to structured output.

## High-Level Overview

```mermaid
flowchart TB
    subgraph Input
        CLI[("CLI Input<br/>(query + flags)")]
    end

    subgraph "Argument Parsing"
        Parser["System.CommandLine<br/>Parser"]
        Validator["Argument Validator"]
    end

    subgraph "Source Registry"
        Registry["ISourceRegistry"]
        
        subgraph "Discovery Sources"
            direction TB
            RegistryUninstall["Registry Uninstall<br/>(HKLM/HKCU)"]
            AppPaths["App Paths<br/>(Registry)"]
            StartMenu["Start Menu<br/>Shortcuts (.lnk)"]
            Process["Process Source<br/>(Running)"]
            PathSearch["PATH Search<br/>(where.exe)"]
            ServicesTask["Services &<br/>Scheduled Tasks"]
            MSIX["MSIX/Store<br/>Packages"]
            HeuristicFS["Heuristic FS<br/>Scan"]
            Scoop["Scoop<br/>Source"]
            Chocolatey["Chocolatey<br/>Source"]
            Winget["Winget<br/>Source"]
        end
    end

    subgraph "Processing Pipeline"
        Aggregator["Parallel Aggregator<br/>(Channel-based)"]
        Deduplicator["Path Deduplicator"]
        RulesEngine["Rules Engine<br/>(YAML)"]
        Ranker["Ranker<br/>(Scoring)"]
        Filter["Result Filters<br/>(type, scope, confidence)"]
        Collapser["Best-Per-Type<br/>Collapser"]
    end

    subgraph "Output Formatting"
        JSON["JSON Serializer<br/>(JsonContext)"]
        CSV["CSV Writer"]
        Text["Text Formatter<br/>(ANSI colors)"]
    end

    subgraph Output
        STDOUT[("stdout")]
        STDERR[("stderr<br/>(diagnostics)")]
    end

    CLI --> Parser
    Parser --> Validator
    Validator -->|SourceOptions| Registry
    
    Registry --> RegistryUninstall
    Registry --> AppPaths
    Registry --> StartMenu
    Registry --> Process
    Registry --> PathSearch
    Registry --> ServicesTask
    Registry --> MSIX
    Registry --> HeuristicFS
    Registry --> Scoop
    Registry --> Chocolatey
    Registry --> Winget
    
    RegistryUninstall -->|AppHit| Aggregator
    AppPaths -->|AppHit| Aggregator
    StartMenu -->|AppHit| Aggregator
    Process -->|AppHit| Aggregator
    PathSearch -->|AppHit| Aggregator
    ServicesTask -->|AppHit| Aggregator
    MSIX -->|AppHit| Aggregator
    HeuristicFS -->|AppHit| Aggregator
    Scoop -->|AppHit| Aggregator
    Chocolatey -->|AppHit| Aggregator
    Winget -->|AppHit| Aggregator
    
    Aggregator --> Deduplicator
    Deduplicator --> RulesEngine
    RulesEngine -->|"Expanded<br/>config/data hits"| Ranker
    Ranker -->|Scored AppHit| Filter
    Filter --> Collapser
    
    Collapser -->|--json| JSON
    Collapser -->|--csv| CSV
    Collapser -->|--text| Text
    
    JSON --> STDOUT
    CSV --> STDOUT
    Text --> STDOUT
    
    Validator -->|errors| STDERR
    Registry -->|--trace timing| STDERR
```

## Detailed Component Dataflow

### 1. Input Processing

```mermaid
flowchart LR
    subgraph "CLI Arguments"
        Args["string[] args"]
    end
    
    subgraph "Parsed Options"
        Query["query: string"]
        Format["format: json|csv|text"]
        Scope["scope: user|machine|both"]
        Types["types: exe|install-dir|config|data"]
        Flags["flags: --all, --evidence, etc."]
    end
    
    subgraph "SourceOptions Record"
        SO["SourceOptions<br/>• UserOnly<br/>• MachineOnly<br/>• Timeout<br/>• Strict<br/>• IncludeEvidence<br/>• OriginalQuery"]
    end
    
    Args --> Query
    Args --> Format
    Args --> Scope
    Args --> Types
    Args --> Flags
    
    Query --> SO
    Scope --> SO
    Flags --> SO
```

### 2. Source Execution (Parallel)

```mermaid
flowchart TB
    subgraph "Query Dispatch"
        Query["Normalized Query"]
        Options["SourceOptions"]
    end
    
    subgraph "Parallel Execution (SemaphoreSlim)"
        direction LR
        T1["Task 1"]
        T2["Task 2"]
        T3["Task 3"]
        TN["Task N"]
    end
    
    subgraph "ISource.QueryAsync"
        S1["Source 1"]
        S2["Source 2"]
        S3["Source 3"]
        SN["Source N"]
    end
    
    subgraph "Channel<AppHit>"
        Chan["Bounded Channel<br/>(backpressure)"]
    end
    
    Query --> T1 & T2 & T3 & TN
    Options --> T1 & T2 & T3 & TN
    
    T1 --> S1
    T2 --> S2
    T3 --> S3
    TN --> SN
    
    S1 -->|"yield AppHit"| Chan
    S2 -->|"yield AppHit"| Chan
    S3 -->|"yield AppHit"| Chan
    SN -->|"yield AppHit"| Chan
```

### 3. AppHit Data Model

```mermaid
classDiagram
    class AppHit {
        +HitType Type
        +Scope Scope
        +string Path
        +string? Version
        +PackageType PackageType
        +string[] Source
        +double Confidence
        +Dictionary~string,string~? Evidence
        +ScoreBreakdown? Breakdown
    }
    
    class HitType {
        <<enumeration>>
        InstallDir
        Exe
        Config
        Data
    }
    
    class Scope {
        <<enumeration>>
        User
        Machine
    }
    
    class PackageType {
        <<enumeration>>
        MSI
        MSIX
        Store
        EXE
        Portable
        ClickOnce
        Squirrel
        Scoop
        Chocolatey
        Winget
        Unknown
    }
    
    class ScoreBreakdown {
        +double Base
        +double NameMatch
        +double TokenCoverage
        +double AliasBonus
        +double EvidenceBoost
        +double MultiSource
        +double Penalties
        +double Total
    }
    
    AppHit --> HitType
    AppHit --> Scope
    AppHit --> PackageType
    AppHit --> ScoreBreakdown
```

### 4. Ranking Pipeline

```mermaid
flowchart TB
    subgraph "Input"
        Hit["Raw AppHit<br/>(Confidence = 0)"]
        Query["Normalized Query"]
    end
    
    subgraph "Ranker.ScoreWithBreakdown()"
        Base["Base Score<br/>(by HitType)"]
        Name["Name Match<br/>(exact/partial/fuzzy)"]
        Token["Token Coverage<br/>(Jaccard + span)"]
        Alias["Alias Matching<br/>(vscode ↔ code)"]
        Evidence["Evidence Boost<br/>(registry, shortcut, process)"]
        Multi["Multi-Source<br/>(harmonic diminishing)"]
        Penalty["Penalties<br/>(temp, cache, generic)"]
    end
    
    subgraph "Output"
        Scored["Scored AppHit<br/>(Confidence ∈ [0,1])"]
        Breakdown["ScoreBreakdown"]
    end
    
    Hit --> Base
    Query --> Name
    Query --> Token
    Query --> Alias
    
    Base --> |+0.08| Evidence
    Name --> |+0.35 max| Evidence
    Token --> |+0.27 max| Evidence
    Alias --> |+0.10 max| Evidence
    Evidence --> |+0.15 max| Multi
    Multi --> |+0.18 max| Penalty
    Penalty --> |−0.20 max| Scored
    
    Scored --> Breakdown
```

### 5. Rules Engine Expansion

```mermaid
flowchart LR
    subgraph "Input"
        ExeHit["Exe AppHit<br/>(e.g., Code.exe)"]
        RulesYAML["apps.default.yaml<br/>(147 app rules)"]
    end
    
    subgraph "RulesEngine.LoadAsync()"
        Match["Match Query<br/>anyOf: [code, vscode]"]
        ConfigPaths["config:<br/>%APPDATA%/Code/User"]
        DataPaths["data:<br/>%LOCALAPPDATA%/..."]
    end
    
    subgraph "Output"
        ConfigHit["Config AppHit"]
        DataHit["Data AppHit"]
    end
    
    ExeHit --> Match
    RulesYAML --> Match
    
    Match --> ConfigPaths
    Match --> DataPaths
    
    ConfigPaths -->|"Expand env vars"| ConfigHit
    DataPaths -->|"Expand env vars"| DataHit
```

### 6. Output Pipeline

```mermaid
flowchart TB
    subgraph "Filtering"
        AllHits["All Scored Hits"]
        TypeFilter["--exe, --config, etc."]
        ScopeFilter["--user, --machine"]
        ConfFilter["--confidence-min"]
        LimitFilter["--limit"]
    end
    
    subgraph "Collapsing (unless --all)"
        ExeCollapse["Top 3 Exe<br/>(distinct dirs)"]
        InstallCollapse["Paired InstallDir"]
        ConfigCollapse["Best Config"]
        DataCollapse["Best Data"]
    end
    
    subgraph "Serialization"
        JSON["JsonSerializer<br/>(source-gen JsonContext)"]
        CSV["Manual CSV<br/>(quoted, escaped)"]
        Text["ANSI Text<br/>([0.92] Exe path)"]
    end
    
    subgraph "Output Enrichment"
        EvidenceFlag["--evidence"]
        BreakdownFlag["--score-breakdown"]
        PkgSourceFlag["--package-source"]
    end
    
    AllHits --> TypeFilter --> ScopeFilter --> ConfFilter --> LimitFilter
    
    LimitFilter -->|"--all"| JSON & CSV & Text
    LimitFilter -->|"default"| ExeCollapse
    ExeCollapse --> InstallCollapse --> ConfigCollapse --> DataCollapse
    DataCollapse --> JSON & CSV & Text
    
    EvidenceFlag --> JSON & CSV & Text
    BreakdownFlag --> JSON & Text
    PkgSourceFlag --> CSV & Text
```

## Source Data Origins

| Source | Windows API / Location | Returns |
|--------|------------------------|---------|
| **RegistryUninstallSource** | `HKLM/HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall` | InstallDir, Exe, Version |
| **AppPathsSource** | `HKLM/HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths` | Exe |
| **StartMenuShortcutSource** | `%APPDATA%\Microsoft\Windows\Start Menu`, `%PROGRAMDATA%\...` | Exe (via .lnk target) |
| **ProcessSource** | `Process.GetProcesses()` | Exe (running) |
| **PathSearchSource** | `%PATH%` directories, `where.exe` | Exe |
| **ServicesTasksSource** | `sc query`, Task Scheduler | Exe |
| **MsixStoreSource** | `Windows.Management.Deployment.PackageManager` | InstallDir, Exe |
| **HeuristicFsSource** | Program Files, AppData (bounded scan) | Exe, InstallDir |
| **ScoopSource** | `~\scoop\apps`, `C:\ProgramData\scoop\apps` | Exe, InstallDir |
| **ChocolateySource** | `C:\ProgramData\chocolatey\lib` | Exe, InstallDir |
| **WingetSource** | `winget list --id` output | Exe, InstallDir |

## Exit Code Flow

```mermaid
flowchart TD
    Start([Start]) --> Parse{Parse Args}
    Parse -->|Error| Exit2([Exit 2])
    Parse -->|--help or no args| Help[Show Help] --> Exit0a([Exit 0])
    Parse -->|Valid| Query[Execute Query]
    Query --> Results{Results Found?}
    Results -->|Yes| Output[Format & Output] --> Exit0b([Exit 0])
    Results -->|No| Exit1([Exit 1])
```

---

*Generated for AppLocate v0.1.5 — See [README.md](../README.md) for CLI usage.*
