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

    /// <param name="path">Full path to index JSON file.</param>
    /// <param name="maxAge">Maximum age before a record should be considered stale (default 1 day).</param>
    public IndexStore(string path, TimeSpan? maxAge = null, JsonSerializerOptions? jsonOptions = null)
    {
        _path = path;
        _maxAge = maxAge ?? TimeSpan.FromDays(1);
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions { WriteIndented = false };
    }

    public IndexFile Load()
    {
        try
        {
            if (!File.Exists(_path)) return IndexFile.CreateEmpty();
            using var fs = File.OpenRead(_path);
            var file = JsonSerializer.Deserialize<IndexFile>(fs, _jsonOptions);
            if (file == null || file.Version != IndexFile.CurrentVersion) return IndexFile.CreateEmpty();
            return file;
        }
        catch { return IndexFile.CreateEmpty(); }
    }

    public void Save(IndexFile file)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
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

    public bool TryGet(IndexFile file, string normalizedQuery, out IndexRecord? record)
    {
        record = file.Records.FirstOrDefault(r => string.Equals(r.Query, normalizedQuery, StringComparison.OrdinalIgnoreCase));
        if (record == null) return false;
        // Staleness check
        if (DateTimeOffset.UtcNow - record.LastRefreshUtc > _maxAge)
            return false;
        return true;
    }
}
