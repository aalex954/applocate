using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AppLocate.Core.Models;

/// <summary>Type of artifact returned for an application.</summary>
public enum HitType 
{ 
    /// <summary>Installation directory (root folder containing the app's binaries).</summary>
    InstallDir, 
    /// <summary>Primary or auxiliary executable file.</summary>
    Exe, 
    /// <summary>Configuration file or directory (user or machine scope).</summary>
    Config, 
    /// <summary>Application data directory (caches, state, profiles).</summary>
    Data 
}

/// <summary>Installation scope of the artifact.</summary>
public enum Scope 
{ 
    /// <summary>Per-user (profile-local) artifact.</summary>
    User, 
    /// <summary>Machine-wide artifact accessible to all users.</summary>
    Machine 
}

/// <summary>Package / distribution mechanism type.</summary>
public enum PackageType 
{ 
    /// <summary>Traditional Windows Installer (MSI) package.</summary>
    MSI, 
    /// <summary>Modern MSIX packaged application.</summary>
    MSIX, 
    /// <summary>Microsoft Store distributed application.</summary>
    Store, 
    /// <summary>Plain executable installer (setup EXE) or unclassified executable source.</summary>
    EXE, 
    /// <summary>Portable (xcopy) distribution with no formal installer.</summary>
    Portable, 
    /// <summary>ClickOnce deployed application.</summary>
    ClickOnce, 
    /// <summary>Squirrel (framework) packaged application.</summary>
    Squirrel, 
    /// <summary>Scoop package manager installation.</summary>
    Scoop, 
    /// <summary>Chocolatey package installation.</summary>
    Chocolatey, 
    /// <summary>Winget package manager installation.</summary>
    Winget, 
    /// <summary>Unknown or not yet classified package type.</summary>
    Unknown 
}

/// <summary>Represents a single located application artifact.</summary>
public sealed record AppHit(
    HitType Type,
    Scope Scope,
    string Path,
    string? Version,
    PackageType PackageType,
    string[] Source,
    double Confidence,
    Dictionary<string,string>? Evidence,
    [property:JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ScoreBreakdown? Breakdown = null
);
