using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AppLocate.Core.Models;

namespace AppLocate.Core.Indexing;

/// <summary>Abstraction for persistent index operations.</summary>
public interface IIndexStore
{
    /// <summary>Loads an index from disk or returns an empty instance if missing or incompatible.</summary>
    IndexFile Load();
    /// <summary>Persists the supplied index to disk (atomic write when possible).</summary>
    void Save(IndexFile file);
    /// <summary>Upserts hits for a normalized query, updating existing entries (touch) or adding new ones.</summary>
    void Upsert(IndexFile file, string normalizedQuery, IEnumerable<AppHit> hits, DateTimeOffset now);
    /// <summary>Attempts to get cached entries for a normalized query.</summary>
    bool TryGet(IndexFile file, string normalizedQuery, out IndexRecord? record);
}

/// <summary>JSON file based index store.</summary>
public sealed class IndexStore : IIndexStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeSpan _maxAge;
    // Simple invalidation: optional external hash (e.g., registry + start menu snapshot) persisted in file future (placeholder for expansion)
    // For now we only expose a hook method that callers could use once file schema extended.

    /// <param name="path">Full path to index JSON file.</param>
    /// <param name="maxAge">Maximum age before a record should be considered stale and thus ignored (default 1 day).</param>
    /// <param name="jsonOptions">Optional custom <see cref="JsonSerializerOptions"/>; when null a minimal options set is created.</param>
    public IndexStore(string path, TimeSpan? maxAge = null, JsonSerializerOptions? jsonOptions = null)
    {
        _path = path;
        _maxAge = maxAge ?? TimeSpan.FromDays(1);
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions { WriteIndented = false };
    }

    /// <summary>
    /// Loads and deserializes the index file from disk. If the file is absent, corrupt,
    /// or version-mismatched a new empty index (current schema) is returned.
    /// </summary>
    public IndexFile Load()
    {
        try
        {
            if (!File.Exists(_path)) return IndexFile.CreateEmpty();
            using var fs = File.OpenRead(_path);
            var file = JsonSerializer.Deserialize<IndexFile>(fs, _jsonOptions);
            if (file == null || file.Version != IndexFile.CurrentVersion) return IndexFile.CreateEmpty();
            // If environment hash mismatched, drop cache (rebuild). For first version with hash, empty indicates legacy.
            var currentHash = ComputeEnvironmentHash();
            if (!string.IsNullOrEmpty(file.EnvironmentHash) && !string.Equals(file.EnvironmentHash, currentHash, StringComparison.Ordinal))
                return IndexFile.CreateEmpty();
            return file;
        }
        catch { return IndexFile.CreateEmpty(); }
    }

    /// <summary>
    /// Persists the supplied index to disk using an atomic temp-file replace strategy.
    /// Swallows IO exceptions silently (cache is best-effort only).
    /// </summary>
    public void Save(IndexFile file)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Ensure environment hash present before save
            var currentHash = ComputeEnvironmentHash();
            if (!string.Equals(file.EnvironmentHash, currentHash, StringComparison.Ordinal))
            {
                file = file with { EnvironmentHash = currentHash };
            }
            var tmp = _path + ".tmp";
            using (var fs = File.Create(tmp))
            {
                JsonSerializer.Serialize(fs, file, _jsonOptions);
            }
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }
        catch { /* swallow persistence errors silently */ }
    }

    /// <summary>
    /// Inserts or updates hit entries for a query. Existing entries are touched (timestamps, sources, confidence) or created.
    /// </summary>
    /// <param name="file">Mutable in-memory index file.</param>
    /// <param name="normalizedQuery">Lower-case normalized query string.</param>
    /// <param name="hits">Scored hits to merge.</param>
    /// <param name="now">Clock value applied to updated timestamps.</param>
    public void Upsert(IndexFile file, string normalizedQuery, IEnumerable<AppHit> hits, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuery)) return;
        if (!TryGet(file, normalizedQuery, out var record) || record == null)
        {
            record = IndexRecord.Create(normalizedQuery, now);
            file.Records.Add(record);
        }
    // Replace record with updated LastRefreshUtc (records are immutable due to positional record model)
    var updatedRecord = new IndexRecord(record.Query, record.Entries, now);
    var recIdx = file.Records.IndexOf(record);
    if (recIdx >= 0) file.Records[recIdx] = updatedRecord; else file.Records.Add(updatedRecord);
    record = updatedRecord;
        foreach (var h in hits)
        {
            var existing = record.Entries.FirstOrDefault(e => e.Type == h.Type && e.Scope == h.Scope && string.Equals(e.Path, h.Path, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                record.Entries.Add(IndexEntry.FromHit(h, now));
            }
            else
            {
                var updated = existing.Touch(h, now);
                var idx = record.Entries.IndexOf(existing);
                record.Entries[idx] = updated;
            }
        }
    }

    /// <summary>
    /// Attempts to retrieve a non-stale cached record for a query.
    /// </summary>
    /// <param name="file">In-memory index file.</param>
    /// <param name="normalizedQuery">Query key.</param>
    /// <param name="record">Resulting record when present and fresh.</param>
    /// <returns><c>true</c> when a fresh record exists; otherwise false.</returns>
    public bool TryGet(IndexFile file, string normalizedQuery, out IndexRecord? record)
    {
        record = file.Records.FirstOrDefault(r => string.Equals(r.Query, normalizedQuery, StringComparison.OrdinalIgnoreCase));
        if (record == null) return false;
        // Staleness check
        if (DateTimeOffset.UtcNow - record.LastRefreshUtc > _maxAge)
            return false;
        return true;
    }

    /// <summary>
    /// Placeholder for external invalidation integration (e.g. comparing Start Menu or registry snapshot hashes).
    /// Currently always returns false (no external invalidation triggered).
    /// </summary>
    public bool IsExternallyInvalidated() => false;

    private static string ComputeEnvironmentHash()
    {
        try
        {
            // Cheap hash inputs: day stamp + ProgramData start menu last write + LocalAppData last write (coarse heuristic)
            var sb = new System.Text.StringBuilder();
            sb.Append(DateTime.UtcNow.Date.ToString("yyyyMMdd"));
            string? sm = Environment.ExpandEnvironmentVariables("%ProgramData%\\Microsoft\\Windows\\Start Menu\\Programs");
            if (!string.IsNullOrEmpty(sm) && Directory.Exists(sm))
            {
                try { sb.Append(new DirectoryInfo(sm).LastWriteTimeUtc.Ticks.ToString()); } catch { }
            }
            string? la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(la) && Directory.Exists(la))
            {
                try { sb.Append(new DirectoryInfo(la).LastWriteTimeUtc.Ticks.ToString()); } catch { }
            }
            // Hash
            var data = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            var hash = System.Security.Cryptography.SHA1.HashData(data);
            return Convert.ToHexString(hash);
        }
        catch { return "0"; }
    }
}
