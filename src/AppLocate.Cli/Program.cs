using System.Text.Json;
using System.CommandLine;
using AppLocate.Core.Models;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Sources;
using AppLocate.Core.Ranking;
using AppLocate.Core.Indexing;
using AppLocate.Core.Rules;

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
        var indexPathOpt = new Option<string>("--index-path") { Description = "Custom index file path (default %LOCALAPPDATA%/AppLocate/index.json)" };
        var refreshIndexOpt = new Option<bool>("--refresh-index") { Description = "Force refresh index for this query (ignore cached)" };
        var evidenceOpt = new Option<bool>("--evidence") { Description = "Include evidence keys when available" };
        var verboseOpt = new Option<bool>("--verbose") { Description = "Verbose diagnostics (warnings)" };
        var noColorOpt = new Option<bool>("--no-color") { Description = "Disable ANSI colors" };
        var root = new RootCommand("Locate application installation directories, executables, and config/data paths")
        {
            queryArg,
            jsonOpt, csvOpt, textOpt, userOpt, machineOpt, strictOpt,
            limitOpt, confMinOpt, timeoutOpt, indexPathOpt, refreshIndexOpt, evidenceOpt, verboseOpt, noColorOpt
        };
        // Manual token extraction (robust multi-word + -- sentinel)
        var parse = root.Parse(args);
        var tokens = parse.Tokens;
        if (parse.Errors?.Count > 0)
        {
            Console.Error.WriteLine(string.Join(Environment.NewLine, parse.Errors.Select(e => e.Message)));
            return 2;
        }
        bool HasRaw(string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        if (HasRaw("-h") || HasRaw("--help"))
        {
            Console.WriteLine("applocate <query> [options]\n" +
                              "  --json                Output JSON\n" +
                              "  --csv                 Output CSV\n" +
                              "  --text                Force text (default)\n" +
                              "  --user                User-scope only\n" +
                              "  --machine             Machine-scope only\n" +
                              "  --strict              Exact token match (no fuzzy)\n" +
                              "  --limit <n>           Limit results\n" +
                              "  --confidence-min <f>  Min confidence 0-1\n" +
                              "  --timeout <sec>       Per-source timeout (default 5)\n" +
                              "  --index-path <file>   Custom index file path\n" +
                              "  --refresh-index       Ignore cached results\n" +
                              "  --evidence            Include evidence keys\n" +
                              "  --verbose             Verbose diagnostics\n" +
                              "  --no-color            Disable ANSI colors\n" +
                              "  --                    Treat following tokens as literal query");
            return 0;
        }
        // If user provided -- sentinel, everything after is treated as query (including dashes)
        int sentinelIdx = Array.IndexOf(args, "--");
        string? query = null;
        if (sentinelIdx >= 0 && sentinelIdx < args.Length - 1)
        {
            query = string.Join(' ', args.Skip(sentinelIdx + 1));
        }
        else
        {
            // Collect argument tokens that are not option values
            var valueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "--limit","--confidence-min","--timeout","--index-path" };
            var parts = new List<string>();
            for (int i = 0; i < tokens.Count; i++)
            {
                var tk = tokens[i];
                if (tk.Type != System.CommandLine.Parsing.TokenType.Argument) continue;
                if (tk.Value.StartsWith('-')) continue; // flag itself
                // Skip if this token is a value for the previous option that expects a value
                if (i > 0 && valueOptions.Contains(tokens[i - 1].Value)) continue;
                // Skip if this token is numeric and immediately preceded by a known numeric option name (already covered above but double-safe)
                parts.Add(tk.Value);
            }
            if (parts.Count > 0) query = string.Join(' ', parts);
            // Fallback: first non-dash arg
            query ??= args.FirstOrDefault(a => !a.StartsWith('-'));
        }
        if (string.IsNullOrWhiteSpace(query)) { Console.Error.WriteLine("Missing <query>. Usage: applocate <query> [options] <name>"); return 2; }

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
        bool refreshIndex = Has("--refresh-index");
        string? indexPath = null;
        // Extract index path argument manually
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            if (string.Equals(tokens[i].Value, "--index-path", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = tokens[i + 1].Value;
                if (!candidate.StartsWith('-')) indexPath = candidate;
            }
        }

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
        if (limit.HasValue && limit.Value < 0)
        {
            Console.Error.WriteLine("--limit must be >= 0");
            return 2;
        }
        double confidenceMin = DoubleAfter("--confidence-min") ?? 0;
        if (confidenceMin < 0 || confidenceMin > 1)
        {
            Console.Error.WriteLine("--confidence-min must be between 0 and 1");
            return 2;
        }
        int timeout = IntAfter("--timeout") ?? 5;
        if (timeout <= 0)
        {
            Console.Error.WriteLine("--timeout must be > 0");
            return 2;
        }
        if (timeout > 300)
        {
            Console.Error.WriteLine("--timeout too large (max 300 seconds)");
            return 2;
        }
        if (!noColor && (Console.IsOutputRedirected || Console.IsErrorRedirected)) noColor = true;
        return await ExecuteAsync(query, json, csv, text, user, machine, strict, limit, confidenceMin, timeout, evidence, verbose, noColor, indexPath, refreshIndex);
    }

    private static async Task<int> ExecuteAsync(string query, bool json, bool csv, bool text, bool user, bool machine, bool strict, int? limit, double confidenceMin, int timeoutSeconds, bool evidence, bool verbose, bool noColor, string? indexPath, bool refreshIndex)
    {
        if (string.IsNullOrWhiteSpace(query)) return 2;
        var options = new SourceOptions(user, machine, TimeSpan.FromSeconds(timeoutSeconds), strict, evidence);
        var normalized = Normalize(query);

        // Index load + short-circuit
        string defaultIndexPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppLocate", "index.json");
        string effectiveIndexPath = string.IsNullOrWhiteSpace(indexPath) ? defaultIndexPath : indexPath;
        IndexStore? indexStore = null; IndexFile? indexFile = null; bool servedFromCache = false;
        try
        {
            indexStore = new IndexStore(effectiveIndexPath);
            indexFile = indexStore.Load();
            if (!refreshIndex && indexStore.TryGet(indexFile, normalized, out var rec) && rec != null)
            {
                var cachedHits = rec.Entries.Select(e => new AppHit(e.Type, e.Scope, e.Path, e.Version, e.PackageType, e.Source, e.Confidence, null)).ToList();
                var filteredCached = cachedHits.Where(h => h.Confidence >= confidenceMin).OrderByDescending(h => h.Confidence).ToList();
                if (limit.HasValue) filteredCached = filteredCached.Take(limit.Value).ToList();
                if (filteredCached.Count > 0)
                {
                    EmitResults(filteredCached, json, csv, text, noColor);
                    servedFromCache = true;
                    return 0;
                }
                // Empty-cache short-circuit: if record exists, has zero entries and we are not refreshing, treat as a known miss.
                if (rec.Entries.Count == 0)
                {
                    if (verbose) Console.Error.WriteLine("[info] cache short-circuit: known empty result set");
                    return 1; // no matches (cached)
                }
            }
        }
        catch (Exception ex) { if (verbose) Console.Error.WriteLine($"[warn] index load failed: {ex.Message}"); }

    var hits = new List<AppHit>();
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

        // Rule-based expansion (config/data heuristics)
        try
        {
            var rulesPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "rules", "apps.default.yaml");
            if (!File.Exists(rulesPath))
            {
                // fallback to relative from current working dir
                var alt = Path.Combine(Directory.GetCurrentDirectory(), "rules", "apps.default.yaml");
                if (File.Exists(alt)) rulesPath = alt; else rulesPath = string.Empty;
            }
            if (!string.IsNullOrEmpty(rulesPath))
            {
                var engine = new RulesEngine();
                var loaded = await engine.LoadAsync(rulesPath, CancellationToken.None);
                if (loaded.Count > 0)
                {
                    // Determine if any rule matches query tokens or existing exe/install hits
                    var allNames = hits.Select(h => Path.GetFileNameWithoutExtension(h.Path)?.ToLowerInvariant()).Where(s => !string.IsNullOrEmpty(s)).ToHashSet();
                    foreach (var rule in loaded)
                    {
                        bool match = rule.MatchAnyOf.Any(m =>
                            string.Equals(m, query, StringComparison.OrdinalIgnoreCase) ||
                            allNames.Contains(m.ToLowerInvariant()));
                        if (!match) continue;
                        foreach (var cfg in rule.Config)
                        {
                            var expanded = Environment.ExpandEnvironmentVariables(cfg.Replace('/', Path.DirectorySeparatorChar));
                            hits.Add(new AppHit(HitType.Config, Scope.User, expanded, null, PackageType.Unknown, new[]{"Rules"}, 0, new System.Collections.Generic.Dictionary<string,string>{{"Rule","config"}}));
                        }
                        foreach (var dat in rule.Data)
                        {
                            var expanded = Environment.ExpandEnvironmentVariables(dat.Replace('/', Path.DirectorySeparatorChar));
                            hits.Add(new AppHit(HitType.Data, Scope.User, expanded, null, PackageType.Unknown, new[]{"Rules"}, 0, new System.Collections.Generic.Dictionary<string,string>{{"Rule","data"}}));
                        }
                    }
                }
            }
        }
        catch (Exception rex) { if (verbose) Console.Error.WriteLine($"[warn] rules expansion failed: {rex.Message}"); }

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
        var scored = mergedMap.Values.Select(h => h with { Confidence = Ranker.Score(normalized, h) }).ToList();
        var filtered = scored.Where(h => h.Confidence >= confidenceMin).OrderByDescending(h => h.Confidence).ToList();
        if (limit.HasValue) filtered = filtered.Take(limit.Value).ToList();
        if (filtered.Count == 0)
        {
            // Persist an empty record to establish index presence for the query if not cached already.
            try
            {
                if (!servedFromCache && indexStore != null && indexFile != null)
                {
                    indexStore.Upsert(indexFile, normalized, Array.Empty<AppHit>(), DateTimeOffset.UtcNow);
                    indexStore.Save(indexFile);
                }
                // Fallback: if save silently failed (file still missing) attempt minimal manual write.
                if (!File.Exists(effectiveIndexPath))
                {
                    var minimal = new IndexFile(IndexFile.CurrentVersion, new List<IndexRecord>{ IndexRecord.Create(normalized, DateTimeOffset.UtcNow) }, null);
                    Directory.CreateDirectory(Path.GetDirectoryName(effectiveIndexPath)!);
                    File.WriteAllText(effectiveIndexPath, System.Text.Json.JsonSerializer.Serialize(minimal));
                }
            }
            catch (Exception ex) { if (verbose) Console.Error.WriteLine($"[warn] index save failed: {ex.Message}"); }
            return 1;
        }
        EmitResults(filtered, json, csv, text, noColor);
        if (!servedFromCache && indexStore != null && indexFile != null)
        {
            try
            {
                indexStore.Upsert(indexFile, normalized, filtered, DateTimeOffset.UtcNow);
                indexStore.Save(indexFile);
                if (!File.Exists(effectiveIndexPath))
                {
                    // Defensive ensure
                    Directory.CreateDirectory(Path.GetDirectoryName(effectiveIndexPath)!);
                    File.WriteAllText(effectiveIndexPath, System.Text.Json.JsonSerializer.Serialize(indexFile));
                }
            }
            catch (Exception ex) { if (verbose) Console.Error.WriteLine($"[warn] index save failed: {ex.Message}"); }
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

    private static void EmitResults(List<AppHit> filtered, bool json, bool csv, bool text, bool noColor)
    {
        if (filtered.Count == 0) return;
        if (json)
        {
            var jsonOut = JsonSerializer.Serialize(filtered, AppLocateJsonContext.Default.IReadOnlyListAppHit);
            Console.Out.WriteLine(jsonOut);
            return;
        }
        if (csv)
        {
            Console.Out.WriteLine("Type,Scope,Path,Version,PackageType,Confidence");
            foreach (var h in filtered)
                Console.Out.WriteLine($"{h.Type},{h.Scope},\"{h.Path}\",{h.Version},{h.PackageType},{h.Confidence:0.###}");
            return;
        }
        if (text)
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
    }
}
