using System.Text.Json;
using AppLocate.Core.Models;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Sources;
using AppLocate.Core.Ranking;

namespace AppLocate.Cli;

internal static class Program
{
    private static readonly ISource[] _sources =
    [
        new RegistryUninstallSource(),
        new AppPathsSource(),
        new StartMenuShortcutSource(),
        new ProcessSource(),
        new PathSearchSource(),
        new MsixStoreSource(),
        new HeuristicFsSource()
    ];

    public static async Task<int> Main(string[] args) => await RunAsync(args);

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0) return 2;
        // Very minimal argument parsing (placeholder until proper System.CommandLine implementation).
        var json = args.Contains("--json");
        var csv = args.Contains("--csv");
        var text = args.Contains("--text") || (!json && !csv);
        bool HasFlag(string f) => args.Contains(f);
        int? GetInt(string name)
        {
            var idx = Array.IndexOf(args, name);
            if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out var v)) return v;
            return null;
        }
        double? GetDouble(string name)
        {
            var idx = Array.IndexOf(args, name);
            if (idx >= 0 && idx + 1 < args.Length && double.TryParse(args[idx + 1], out var v)) return v;
            return null;
        }
        // First non-flag token is query
        var query = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (string.IsNullOrWhiteSpace(query)) return 2;
        var user = HasFlag("--user");
        var machine = HasFlag("--machine");
        var strict = HasFlag("--strict");
        var limit = GetInt("--limit");
        var confidenceMin = GetDouble("--confidence-min") ?? 0;
        var timeout = GetInt("--timeout") ?? 5;
        if (timeout <= 0) timeout = 5;
        var evidence = HasFlag("--evidence");
        var verbose = HasFlag("--verbose");
        var options = new SourceOptions(user, machine, TimeSpan.FromSeconds(timeout), strict, evidence);
        var hits = new List<AppHit>();
        var normalized = Normalize(query);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        foreach (var source in _sources)
        {
            try
            {
                var srcCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                srcCts.CancelAfter(options.Timeout);
                await foreach (var hit in source.QueryAsync(normalized, options, srcCts.Token))
                {
                    hits.Add(hit); // defer scoring until after de-dup
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { break; }
            catch (Exception ex) { if (verbose) Console.Error.WriteLine($"[warn] {source.Name} failed: {ex.Message}"); }
        }

        // De-duplicate & merge evidence/sources by (Type,Scope,Path) case-insensitive path key.
        var mergedMap = new Dictionary<string, AppHit>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in hits)
        {
            var key = $"{h.Type}|{h.Scope}|{h.Path}";
            if (!mergedMap.TryGetValue(key, out var existing))
            {
                mergedMap[key] = h with { Source = (string[])h.Source.Clone() };
                continue;
            }
            // Merge: combine unique sources; merge evidence keys by accumulating distinct values.
            var srcSet = new HashSet<string>(existing.Source, StringComparer.OrdinalIgnoreCase);
            foreach (var s in h.Source) srcSet.Add(s);
            Dictionary<string,string>? evidenceMerged = null;
            if (existing.Evidence != null || h.Evidence != null)
            {
                evidenceMerged = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                if (existing.Evidence != null)
                {
                    foreach (var kv in existing.Evidence) evidenceMerged[kv.Key] = kv.Value;
                }
                if (h.Evidence != null)
                {
                    foreach (var kv in h.Evidence)
                    {
                        if (evidenceMerged.TryGetValue(kv.Key, out var existingVal))
                        {
                            // If same value already present (case-insensitive compare), skip.
                            if (existingVal.Equals(kv.Value, StringComparison.OrdinalIgnoreCase)) continue;
                            // If existing contains pipe-separated list, check for presence; else append.
                            var parts = existingVal.Split('|');
                            if (!parts.Any(p => p.Equals(kv.Value, StringComparison.OrdinalIgnoreCase)))
                            {
                                evidenceMerged[kv.Key] = existingVal + "|" + kv.Value;
                            }
                        }
                        else
                        {
                            evidenceMerged[kv.Key] = kv.Value;
                        }
                    }
                }
            }
            // Keep existing for now; will rescore after loop.
            mergedMap[key] = existing with { Source = srcSet.ToArray(), Evidence = evidenceMerged };
        }
        // Apply ranking scores now on merged hits.
        var scored = mergedMap.Values.Select(h => h with { Confidence = Ranker.Score(normalized, h) }).ToList();
        var filtered = scored.Where(h => h.Confidence >= confidenceMin).OrderByDescending(h => h.Confidence).ToList();
        if (limit.HasValue) filtered = filtered.Take(limit.Value).ToList();
        if (filtered.Count == 0) return 1;
        if (json)
        {
            var jsonOut = JsonSerializer.Serialize(filtered, AppLocateJsonContext.Default.IReadOnlyListAppHit);
            Console.Out.WriteLine(jsonOut);
        }
        else if (csv)
        {
            Console.Out.WriteLine("Type,Scope,Path,Version,PackageType,Confidence");
            foreach (var h in filtered)
                Console.Out.WriteLine($"{h.Type},{h.Scope},\"{h.Path}\",{h.Version},{h.PackageType},{h.Confidence:0.###}");
        }
        else if (text)
        {
            foreach (var h in filtered)
                Console.Out.WriteLine($"[{h.Confidence:0.00}] {h.Type} {h.Path}");
        }
        return 0;
    }

    private static string Normalize(string query)
    {
        return string.Join(' ', query.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
