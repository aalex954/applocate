using System.Collections.Generic;

namespace AppLocate.Core.Models;

/// <summary>Type of artifact returned for an application.</summary>
public enum HitType { InstallDir, Exe, Config, Data }

/// <summary>Installation scope of the artifact.</summary>
public enum Scope { User, Machine }

/// <summary>Package / distribution mechanism type.</summary>
public enum PackageType { MSI, MSIX, Store, EXE, Portable, ClickOnce, Squirrel, Scoop, Chocolatey, Winget, Unknown }

/// <summary>Represents a single located application artifact.</summary>
public sealed record AppHit(
    HitType Type,
    Scope Scope,
    string Path,
    string? Version,
    PackageType PackageType,
    string[] Source,
    double Confidence,
    Dictionary<string,string>? Evidence
);
