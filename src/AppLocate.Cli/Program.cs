using System.Text.Json;
using System.CommandLine;
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
    new ServicesTasksSource(),
        new MsixStoreSource(),
        new HeuristicFsSource()
    ];

    public static async Task<int> Main(string[] args) => await RunAsync(args); // Manual parsing (System.CommandLine deferred)

    // Retained manual parser; System.CommandLine integration postponed.
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0) return 2;
        // Minimal System.CommandLine usage: parse query + --json; fall back to manual for rest.
        string? query = null;
        bool json = false;
        bool csv = false, text = false, user = false, machine = false, strict = false, evidence = false, verbose = false;
        int? limit = null; double confidenceMin = 0; int timeout = 5;
        try
        {
            var queryArg = new Argument<string>("query");
            var jsonOpt = new Option<bool>("--json");
            var csvOpt = new Option<bool>("--csv");
            var textOpt = new Option<bool>("--text");
            var userOpt = new Option<bool>("--user");
            var machineOpt = new Option<bool>("--machine");
            var strictOpt = new Option<bool>("--strict");
            var limitOpt = new Option<int?>("--limit");
            var confMinOpt = new Option<double?>("--confidence-min");
            var timeoutOpt = new Option<int?>("--timeout");
            var evidenceOpt = new Option<bool>("--evidence");
            var verboseOpt = new Option<bool>("--verbose");
            var root = new RootCommand { queryArg, jsonOpt, csvOpt, textOpt, userOpt, machineOpt, strictOpt, limitOpt, confMinOpt, timeoutOpt, evidenceOpt, verboseOpt };
            var parse = root.Parse(args);
            // Build token list for quick lookups
            var tokens = parse.Tokens.ToList();
            // Query token: first argument token not starting with '-'
            var argToken = tokens.FirstOrDefault(t => t.Type == System.CommandLine.Parsing.TokenType.Argument && !t.Value.StartsWith("-"));
            if (argToken != null) query = argToken.Value;
            bool Has(string flag) => tokens.Any(t => string.Equals(t.Value, flag, StringComparison.OrdinalIgnoreCase));
            json = Has("--json");
            csv = !json && Has("--csv");
            text = !json && !csv && (Has("--text") || true); // default to text
            user = Has("--user");
            machine = Has("--machine");
            strict = Has("--strict");
            evidence = Has("--evidence");
            verbose = Has("--verbose");
            // For value options, locate the token after the flag if numeric.
            int? AfterInt(string flag)
            {
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (string.Equals(tokens[i].Value, flag, StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                    {
                        if (int.TryParse(tokens[i + 1].Value, out var v)) return v;
                    }
                }
                return null;
            }
            double? AfterDouble(string flag)
            {
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (string.Equals(tokens[i].Value, flag, StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                    {
                        if (double.TryParse(tokens[i + 1].Value, out var v)) return v;
                    }
                }
                return null;
            }
            limit = AfterInt("--limit");
            confidenceMin = AfterDouble("--confidence-min") ?? 0;
            timeout = AfterInt("--timeout") ?? 5;
        }
        catch
        {
            // Fallback manual parse (rare path).
            query ??= args.FirstOrDefault(a => !a.StartsWith("--"));
            bool Has(string f) => args.Contains(f);
            json = args.Contains("--json");
            csv = !json && args.Contains("--csv");
            text = !json && !csv; // default
            user = Has("--user"); machine = Has("--machine"); strict = Has("--strict"); evidence = Has("--evidence"); verbose = Has("--verbose");
            int idx(string n) => Array.IndexOf(args, n);
            int? parseInt(string n) { var i = idx(n); return (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v)) ? v : null; }
            double? parseDouble(string n) { var i = idx(n); return (i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], out var v)) ? v : null; }
            limit = parseInt("--limit"); confidenceMin = parseDouble("--confidence-min") ?? 0; timeout = parseInt("--timeout") ?? 5;
        }
        if (timeout <= 0) timeout = 5;
        query ??= args.FirstOrDefault(a => !a.StartsWith("--"));
        if (string.IsNullOrWhiteSpace(query)) return 2;
        return await ExecuteAsync(query, json, csv, text, user, machine, strict, limit, confidenceMin, timeout, evidence, verbose);
    }

    private static async Task<int> ExecuteAsync(string query, bool json, bool csv, bool text, bool user, bool machine, bool strict, int? limit, double confidenceMin, int timeoutSeconds, bool evidence, bool verbose)
    {
        if (string.IsNullOrWhiteSpace(query)) return 2;
        var options = new SourceOptions(user, machine, TimeSpan.FromSeconds(timeoutSeconds), strict, evidence);
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
