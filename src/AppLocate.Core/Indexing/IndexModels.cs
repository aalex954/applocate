using System;
using System.Collections.Generic;
using AppLocate.Core.Models;

namespace AppLocate.Core.Indexing;

/// <summary>Represents a single cached hit enriched with minimal metadata for persistence.</summary>
public sealed record IndexEntry(
    HitType Type,
    Scope Scope,
    string Path,
    string? Version,
    PackageType PackageType,
    string[] Source,
    double Confidence,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc
)
{
    /// <summary>Creates an <see cref="IndexEntry"/> from a live <see cref="AppHit"/>.</summary>
    public static IndexEntry FromHit(AppHit h, DateTimeOffset now) => new(
        h.Type, h.Scope, h.Path, h.Version, h.PackageType, h.Source, h.Confidence, now, now);

    /// <summary>Updates LastSeen timestamp and merges sources; returns new immutable instance.</summary>
    public IndexEntry Touch(AppHit h, DateTimeOffset now)
    {
        var srcSet = new HashSet<string>(Source, StringComparer.OrdinalIgnoreCase);
        foreach (var s in h.Source) srcSet.Add(s);
        // Confidence updated to latest scored value.
        return this with { Source = new List<string>(srcSet).ToArray(), Confidence = h.Confidence, LastSeenUtc = now, Version = h.Version ?? this.Version };
    }
}

/// <summary>Represents cached results for a single normalized query string.</summary>
public sealed record IndexRecord(
    string Query,
    List<IndexEntry> Entries,
    DateTimeOffset LastRefreshUtc
)
{
    public static IndexRecord Create(string query, DateTimeOffset now) => new(query, new List<IndexEntry>(), now);
}

/// <summary>Root persisted index file containing versioning and records by query.</summary>
public sealed record IndexFile(
    int Version,
    List<IndexRecord> Records
)
{
    public static IndexFile CreateEmpty(int version = CurrentVersion) => new(version, new List<IndexRecord>());
    public const int CurrentVersion = 1;
}
