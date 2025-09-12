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

    public static async Task<int> Main(string[] args) => await RunAsync(args);

    internal static async Task<int> RunAsync(string[] args)
    {
        // Define grammar for help text only (we still manually extract to remain API-stable)
        var queryArg = new Argument<string>("query") { Description = "Application name or partial (e.g. 'vscode','chrome')" };
        var jsonOpt = new Option<bool>("--json") { Description = "Output results as JSON array" };
        var csvOpt = new Option<bool>("--csv") { Description = "Output results as CSV" };
        var textOpt = new Option<bool>("--text") { Description = "Force text output (default if neither --json nor --csv)" };
        var userOpt = new Option<bool>("--user") { Description = "Limit to user-scope results" };
        var machineOpt = new Option<bool>("--machine") { Description = "Limit to machine-scope results" };
        var strictOpt = new Option<bool>("--strict") { Description = "Disable fuzzy/alias matching (exact tokens only)" };
        var limitOpt = new Option<int?>("--limit") { Description = "Maximum number of results to return" };
    var confMinOpt = new Option<double>("--confidence-min") { Description = "Minimum confidence threshold (0-1)" };
    var timeoutOpt = new Option<int>("--timeout") { Description = "Per-source timeout seconds (default 5)" };
        var evidenceOpt = new Option<bool>("--evidence") { Description = "Include evidence keys when available" };
        var verboseOpt = new Option<bool>("--verbose") { Description = "Verbose diagnostics (warnings)" };
        var noColorOpt = new Option<bool>("--no-color") { Description = "Disable ANSI colors" };
        var root = new RootCommand("Locate application installation directories, executables, and config/data paths")
        {
            queryArg,
            jsonOpt, csvOpt, textOpt, userOpt, machineOpt, strictOpt,
            limitOpt, confMinOpt, timeoutOpt, evidenceOpt, verboseOpt, noColorOpt
        };
        // Manual token extraction
        var parse = root.Parse(args);
        var tokens = parse.Tokens;
        string? query = tokens.FirstOrDefault(t => t.Type == System.CommandLine.Parsing.TokenType.Argument && !t.Value.StartsWith('-'))?.Value
                        ?? args.FirstOrDefault(a => !a.StartsWith('-'));
    if (string.IsNullOrWhiteSpace(query)) { Console.Error.WriteLine("Missing <query>. Usage: applocate <query> [options]"); return 2; }

        bool Has(string flag) => tokens.Any(t => string.Equals(t.Value, flag, StringComparison.OrdinalIgnoreCase));
        bool json = Has("--json");
        bool csv = !json && Has("--csv");
        bool text = !json && !csv; // default text
        bool user = Has("--user");
        bool machine = Has("--machine");
        bool strict = Has("--strict");
        bool evidence = Has("--evidence");
        bool verbose = Has("--verbose");
        bool noColor = Has("--no-color");

        int? IntAfter(string name)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i].Value, name, StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                {
                    if (int.TryParse(tokens[i + 1].Value, out var v)) return v;
                }
            }
            return null;
        }
        double? DoubleAfter(string name)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i].Value, name, StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                {
                    if (double.TryParse(tokens[i + 1].Value, out var v)) return v;
                }
            }
            return null;
        }
        int? limit = IntAfter("--limit");
    double confidenceMin = DoubleAfter("--confidence-min") ?? 0;
        if (confidenceMin < 0 || confidenceMin > 1)
        {
            Console.Error.WriteLine("--confidence-min must be between 0 and 1");
            return 2;
        }
    int timeout = IntAfter("--timeout") ?? 5; if (timeout <= 0) timeout = 5;
        if (!noColor && (Console.IsOutputRedirected || Console.IsErrorRedirected)) noColor = true;
        return await ExecuteAsync(query, json, csv, text, user, machine, strict, limit, confidenceMin, timeout, evidence, verbose, noColor);
    }

    private static async Task<int> ExecuteAsync(string query, bool json, bool csv, bool text, bool user, bool machine, bool strict, int? limit, double confidenceMin, int timeoutSeconds, bool evidence, bool verbose, bool noColor)
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
            {
                if (noColor)
                {
                    Console.Out.WriteLine($"[{h.Confidence:0.00}] {h.Type} {h.Path}");
                    continue;
                }
                var prevColor = Console.ForegroundColor;
                Console.ForegroundColor = ConfidenceColor(h.Confidence);
                Console.Out.Write($"[{h.Confidence:0.00}]");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Out.Write($" {h.Type}");
                Console.ForegroundColor = prevColor;
                Console.Out.WriteLine($" {h.Path}");
            }
        }
        return 0;
    }

    private static string Normalize(string query)
    {
        return string.Join(' ', query.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    // Manual PrintHelp removed: System.CommandLine generates help.

    private static ConsoleColor ConfidenceColor(double c)
    {
        if (c >= 0.80) return ConsoleColor.Green;
        if (c >= 0.50) return ConsoleColor.Yellow;
        return ConsoleColor.DarkGray;
    }

    // Wrap helper removed (obsolete with generated help)
}
