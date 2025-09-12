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
        if (args.Length == 0)
        {
            PrintHelp();
            return 2;
        }
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return 0;
        }
        // Minimal System.CommandLine usage: parse query + --json; fall back to manual for rest.
        string? query = null;
        bool json = false;
    bool csv = false, text = false, user = false, machine = false, strict = false, evidence = false, verbose = false, noColor = false;
        int? limit = null; double confidenceMin = 0; int timeout = 5;
        try
        {
            var queryArg = new Argument<string>("query") { Description = "Application name or partial to search (e.g. 'vscode', 'chrome')" };
            var jsonOpt = new Option<bool>("--json") { Description = "Output results as JSON array" };
            var csvOpt = new Option<bool>("--csv") { Description = "Output results as CSV (Type,Scope,Path,...)" };
            var textOpt = new Option<bool>("--text") { Description = "Output in human-readable text (default)" };
            var userOpt = new Option<bool>("--user") { Description = "Limit to user-scope results" };
            var machineOpt = new Option<bool>("--machine") { Description = "Limit to machine-scope results" };
            var strictOpt = new Option<bool>("--strict") { Description = "Disable fuzzy/alias matching (exact tokens only)" };
            var limitOpt = new Option<int?>("--limit") { Description = "Maximum number of results to return" };
            var confMinOpt = new Option<double?>("--confidence-min") { Description = "Minimum confidence threshold (0-1)" };
            var timeoutOpt = new Option<int?>("--timeout") { Description = "Per-source timeout seconds (default 5)" };
            var evidenceOpt = new Option<bool>("--evidence") { Description = "Include evidence keys when available" };
            var verboseOpt = new Option<bool>("--verbose") { Description = "Verbose diagnostics (warnings)" };
            var noColorOpt = new Option<bool>("--no-color") { Description = "Disable ANSI colors" };
            var root = new RootCommand { queryArg, jsonOpt, csvOpt, textOpt, userOpt, machineOpt, strictOpt, limitOpt, confMinOpt, timeoutOpt, evidenceOpt, verboseOpt, noColorOpt };
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
            noColor = Has("--no-color");
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
            user = Has("--user"); machine = Has("--machine"); strict = Has("--strict"); evidence = Has("--evidence"); verbose = Has("--verbose"); noColor = Has("--no-color");
            int idx(string n) => Array.IndexOf(args, n);
            int? parseInt(string n) { var i = idx(n); return (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v)) ? v : null; }
            double? parseDouble(string n) { var i = idx(n); return (i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], out var v)) ? v : null; }
            limit = parseInt("--limit"); confidenceMin = parseDouble("--confidence-min") ?? 0; timeout = parseInt("--timeout") ?? 5;
        }
        if (timeout <= 0) timeout = 5;
        query ??= args.FirstOrDefault(a => !a.StartsWith("--"));
        if (string.IsNullOrWhiteSpace(query)) return 2;
        // Auto-disable color if redirected.
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

    private static void PrintHelp(bool noColor = false)
    {
        var rows = new (string Opt, string Desc)[]
        {
            ("--json", "Output results as JSON array"),
            ("--csv", "Output results as CSV"),
            ("--text", "Output text (default)"),
            ("--user", "Only user-scope hits"),
            ("--machine", "Only machine-scope hits"),
            ("--strict", "Disable fuzzy/alias matching"),
            ("--limit <N>", "Limit number of results"),
            ("--confidence-min <X>", "Minimum confidence (0-1)"),
            ("--timeout <sec>", "Per-source timeout (default 5)"),
            ("--evidence", "Include evidence fields"),
            ("--verbose", "Verbose diagnostics"),
            ("--no-color", "Disable ANSI colors"),
            ("-h, --help", "Show this help and exit")
        };
        int consoleWidth;
        try { consoleWidth = Console.WindowWidth; } catch { consoleWidth = 100; }
        if (consoleWidth < 40) consoleWidth = 80; // minimal sane width
        var usage = "applocate <query> [options]\n\nOptions:";
        if (noColor || Console.IsOutputRedirected) Console.Out.WriteLine(usage);
        else
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Out.WriteLine("applocate <query> [options]");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Out.WriteLine();
            Console.Out.WriteLine("Options:");
            Console.ForegroundColor = prev;
        }
        int optColWidth = rows.Max(r => r.Opt.Length) + 4; // padding
        if (optColWidth > 32) optColWidth = 32; // cap option column
        int descWidth = consoleWidth - optColWidth - 2;
        foreach (var row in rows)
        {
            var wrapped = Wrap(row.Desc, descWidth).ToArray();
            if (wrapped.Length == 0) continue;
            if (noColor || Console.IsOutputRedirected)
            {
                Console.Out.WriteLine($"  {row.Opt.PadRight(optColWidth)}{wrapped[0]}");
            }
            else
            {
                var prev = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Out.Write("  " + row.Opt.PadRight(optColWidth));
                Console.ForegroundColor = prev;
                Console.Out.WriteLine(wrapped[0]);
            }
            for (int i = 1; i < wrapped.Length; i++)
            {
                Console.Out.WriteLine($"  {new string(' ', optColWidth)}{wrapped[i]}");
            }
        }
        Console.Out.WriteLine();
        if (!noColor && !Console.IsOutputRedirected)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Out.WriteLine("Examples:");
            Console.ForegroundColor = prev;
        }
        else Console.Out.WriteLine("Examples:");
        Console.Out.WriteLine("  applocate vscode --json --limit 2");
        Console.Out.WriteLine("  applocate 'Google Chrome' --machine --confidence-min 0.7");
    }

    private static ConsoleColor ConfidenceColor(double c)
    {
        if (c >= 0.80) return ConsoleColor.Green;
        if (c >= 0.50) return ConsoleColor.Yellow;
        return ConsoleColor.DarkGray;
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        if (string.IsNullOrEmpty(text) || width <= 10) return new[]{ text };
        var words = text.Split(' ');
        var line = new System.Text.StringBuilder();
        var lines = new List<string>();
        foreach (var w in words)
        {
            if (line.Length == 0)
            {
                line.Append(w);
                continue;
            }
            if (line.Length + 1 + w.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear();
                line.Append(w);
            }
            else
            {
                line.Append(' ').Append(w);
            }
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines;
    }
}
