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
                    var score = Ranker.Score(normalized, hit);
                    hits.Add(hit with { Confidence = score });
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { break; }
            catch (Exception ex) { if (verbose) Console.Error.WriteLine($"[warn] {source.Name} failed: {ex.Message}"); }
        }
        var filtered = hits.Where(h => h.Confidence >= confidenceMin).OrderByDescending(h => h.Confidence).ToList();
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
