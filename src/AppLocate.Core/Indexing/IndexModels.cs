using AppLocate.Core.Models;

namespace AppLocate.Core.Indexing {
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
    ) {
        /// <summary>Creates an <see cref="IndexEntry"/> from a live <see cref="AppHit"/>.</summary>
        public static IndexEntry FromHit(AppHit h, DateTimeOffset now) => new(
            h.Type, h.Scope, h.Path, h.Version, h.PackageType, h.Source, h.Confidence, now, now);

        /// <summary>Updates LastSeen timestamp and merges sources; returns new immutable instance.</summary>
        public IndexEntry Touch(AppHit h, DateTimeOffset now) {
            var srcSet = new HashSet<string>(Source, StringComparer.OrdinalIgnoreCase);
            foreach (var s in h.Source) {
                _ = srcSet.Add(s);
            }
            // Confidence updated to latest scored value.
            return this with { Source = [.. srcSet], Confidence = h.Confidence, LastSeenUtc = now, Version = h.Version ?? Version };
        }
    }

    /// <summary>Represents cached results for a single normalized query string.</summary>
    public sealed record IndexRecord(
        string Query,
        List<IndexEntry> Entries,
        DateTimeOffset LastRefreshUtc
    ) {
        /// <summary>
        /// Factory helper creating a new <see cref="IndexRecord"/> for a normalized query string with no entries.
        /// </summary>
        /// <param name="query">Already normalized (lower-cased, trimmed) query token string.</param>
        /// <param name="now">Timestamp used as the initial <see cref="LastRefreshUtc"/> value.</param>
        /// <returns>New empty record.</returns>
        public static IndexRecord Create(string query, DateTimeOffset now) => new(query, [], now);
    }

    /// <summary>Root persisted index file containing versioning and records by query.</summary>
    public sealed record IndexFile(
        int Version,
        List<IndexRecord> Records,
        string? EnvironmentHash = null
    ) {
        /// <summary>
        /// Creates an empty index with supplied <paramref name="version"/>; normally callers use default (current) version.
        /// </summary>
        /// <param name="version">Schema version for the file (used for forward incompatible format changes).</param>
        /// <returns>Empty index file container.</returns>
        public static IndexFile CreateEmpty(int version = CurrentVersion) => new(version, [], null);
        /// <summary>Current on-disk schema version for <see cref="IndexFile"/> serialization.</summary>
        public const int CurrentVersion = 2;
    }
}
