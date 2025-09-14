using System.Text.Json;
using System.CommandLine;
using AppLocate.Core.Models;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Sources;
using AppLocate.Core.Ranking;
using AppLocate.Core.Indexing;
using AppLocate.Core.Rules;

namespace AppLocate.Cli;

public static class Program
{
    private static ISourceRegistry BuildRegistry()
    {
        // Builder allows future plugin injection (e.g., rule-pack driven or external package managers)
        var builder = new SourceRegistryBuilder()
            .Add(new RegistryUninstallSource())
            .Add(new AppPathsSource())
            .Add(new StartMenuShortcutSource())
            .Add(new ProcessSource())
            .Add(new PathSearchSource())
            .Add(new ServicesTasksSource())
            .Add(new MsixStoreSource())
            .Add(new HeuristicFsSource());
        return builder.Build();
    }

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
    var allOpt = new Option<bool>("--all") { Description = "Return all hits (default returns best per type)" };
    var exeOpt = new Option<bool>("--exe") { Description = "Include only executable hits (can combine with others)" };
    var installDirOpt = new Option<bool>("--install-dir") { Description = "Include only install directory hits" };
    var configOpt = new Option<bool>("--config") { Description = "Include only config hits" };
    var dataOpt = new Option<bool>("--data") { Description = "Include only data hits" };
    var runningOpt = new Option<bool>("--running") { Description = "Include running process exe (enables ProcessSource)" };
    var pidOpt = new Option<int?>("--pid") { Description = "Restrict to a specific process id (implies --running)" };
    var packageSourceOpt = new Option<bool>("--package-source") { Description = "Include package type and raw source list in text/CSV output" };
    var threadsOpt = new Option<int?>("--threads") { Description = "Maximum parallel source tasks (default = logical processors, cap 16)" };
    var traceOpt = new Option<bool>("--trace") { Description = "Emit per-source timing diagnostics" };
        var limitOpt = new Option<int?>("--limit") { Description = "Maximum number of results to return" };
    var confMinOpt = new Option<double>("--confidence-min") { Description = "Minimum confidence threshold (0-1)" };
    var timeoutOpt = new Option<int>("--timeout") { Description = "Per-source timeout seconds (default 5)" };
        var indexPathOpt = new Option<string>("--index-path") { Description = "Custom index file path (default %LOCALAPPDATA%/AppLocate/index.json)" };
        var refreshIndexOpt = new Option<bool>("--refresh-index") { Description = "Force refresh index for this query (ignore cached)" };
        var clearCacheOpt = new Option<bool>("--clear-cache") { Description = "Delete the on-disk index file before running (cache reset)" };
    var evidenceOpt = new Option<bool>("--evidence") { Description = "Include evidence keys when available" };
    // New: selective evidence filtering (comma-separated list); implicitly enables evidence emission
    var evidenceKeysOpt = new Option<string>("--evidence-keys") { Description = "Comma-separated list of evidence keys to include (implies --evidence)" };
        var verboseOpt = new Option<bool>("--verbose") { Description = "Verbose diagnostics (warnings)" };
        var noColorOpt = new Option<bool>("--no-color") { Description = "Disable ANSI colors" };
        var root = new RootCommand("Locate application installation directories, executables, and config/data paths")
        {
            queryArg,
            jsonOpt, csvOpt, textOpt, userOpt, machineOpt, strictOpt, allOpt,
            exeOpt, installDirOpt, configOpt, dataOpt, runningOpt, pidOpt, packageSourceOpt, threadsOpt, traceOpt,
            limitOpt, confMinOpt, timeoutOpt, indexPathOpt, refreshIndexOpt, clearCacheOpt, evidenceOpt, evidenceKeysOpt, verboseOpt, noColorOpt
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
                              "  --all                 Do not collapse to best per type; return all hits\n" +
                              "  --exe                 Filter to exe hits (can combine)\n" +
                              "  --install-dir         Filter to install_dir hits (can combine)\n" +
                              "  --config              Filter to config hits (can combine)\n" +
                              "  --data                Filter to data hits (can combine)\n" +
                              "  --running             Include running processes (process source)\n" +
                              "  --pid <n>             Target specific process id (adds its exe even if name mismatch)\n" +
                              "  --package-source      Show package type & sources in text/CSV output\n" +
                              "  --threads <n>         Max parallel source queries (default logical CPU, cap 16)\n" +
                              "  --trace               Show per-source timing diagnostics\n" +
                              "  --limit <n>           Limit results\n" +
                              "  --confidence-min <f>  Min confidence 0-1\n" +
                              "  --timeout <sec>       Per-source timeout (default 5)\n" +
                              "  --index-path <file>   Custom index file path\n" +
                              "  --refresh-index       Ignore cached results\n" +
                              "  --clear-cache         Delete index file before query (forces full rebuild)\n" +
                              "  --evidence            Include evidence keys\n" +
                              "  --evidence-keys <k1,k2>  Only include specified evidence keys (implies --evidence)\n" +
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
    bool all = Has("--all");
    bool onlyExe = Has("--exe");
    bool onlyInstall = Has("--install-dir");
    bool onlyConfig = Has("--config");
    bool onlyData = Has("--data");
        bool running = Has("--running");
        int? pid = IntAfter("--pid");
        if (pid.HasValue && pid.Value <= 0)
        {
            Console.Error.WriteLine("--pid must be > 0");
            return 2;
        }
        if (pid.HasValue) running = true; // imply
    bool showPackageSources = Has("--package-source");
        int? threads = IntAfter("--threads");
    bool trace = Has("--trace");
        if (threads.HasValue)
        {
            if (threads.Value <= 0) { Console.Error.WriteLine("--threads must be > 0"); return 2; }
            if (threads.Value > 128) { Console.Error.WriteLine("--threads too large (max 128)"); return 2; }
        }
        bool evidence = Has("--evidence");
        string? evidenceKeysRaw = null;
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            if (string.Equals(tokens[i].Value, "--evidence-keys", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = tokens[i + 1].Value;
                if (!candidate.StartsWith('-')) evidenceKeysRaw = candidate; // raw CSV
            }
        }
        HashSet<string>? evidenceKeyFilter = null;
        if (!string.IsNullOrWhiteSpace(evidenceKeysRaw))
        {
            evidence = true; // implicit enable
            evidenceKeyFilter = new HashSet<string>(evidenceKeysRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s), StringComparer.OrdinalIgnoreCase);
        }
        bool verbose = Has("--verbose");
        bool noColor = Has("--no-color");
        bool refreshIndex = Has("--refresh-index");
    bool clearCache = Has("--clear-cache");
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
    return await ExecuteAsync(query, json, csv, text, user, machine, strict, all, onlyExe, onlyInstall, onlyConfig, onlyData, running, pid, showPackageSources, threads, limit, confidenceMin, timeout, evidence, evidenceKeyFilter, verbose, trace, noColor, indexPath, refreshIndex, clearCache);
    }

    private static async Task<int> ExecuteAsync(string query, bool json, bool csv, bool text, bool user, bool machine, bool strict, bool all, bool onlyExe, bool onlyInstall, bool onlyConfig, bool onlyData, bool running, int? pid, bool showPackageSources, int? threads, int? limit, double confidenceMin, int timeoutSeconds, bool evidence, HashSet<string>? evidenceKeyFilter, bool verbose, bool trace, bool noColor, string? indexPath, bool refreshIndex, bool clearCache)
    {
        if (string.IsNullOrWhiteSpace(query)) return 2;
        if (verbose)
        {
            try
            {
                Console.Error.WriteLine($"[verbose] query='{query}' strict={strict} all={all} onlyExe={onlyExe} onlyInstall={onlyInstall} onlyConfig={onlyConfig} onlyData={onlyData} running={running} pid={(pid?.ToString() ?? "-")} pkgSrc={showPackageSources} evidence={evidence} evidenceKeys={(evidenceKeyFilter==null?"(all|none)":string.Join(',',evidenceKeyFilter))} json={json} csv={csv} text={text} confMin={confidenceMin} limit={(limit?.ToString() ?? "-")} threads={(threads?.ToString() ?? "-")} idxPath={(indexPath ?? "(default)")} refreshIndex={refreshIndex} clearCache={clearCache}");
            }
            catch { }
        }
        var options = new SourceOptions(user, machine, TimeSpan.FromSeconds(timeoutSeconds), strict, evidence);
        var normalized = Normalize(query);

        // Index load + short-circuit
        string defaultIndexPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppLocate", "index.json");
        string effectiveIndexPath = string.IsNullOrWhiteSpace(indexPath) ? defaultIndexPath : indexPath;
        IndexStore? indexStore = null; IndexFile? indexFile = null; bool servedFromCache = false;
        // Composite key: query + flags that affect result shape (scope filters, strictness, running, type filters, confidence min rounding)
        string compositeKey = BuildCompositeKey(normalized, user, machine, strict, running, pid, onlyExe, onlyInstall, onlyConfig, onlyData, confidenceMin);
        try
        {
            indexStore = new IndexStore(effectiveIndexPath);
            if (clearCache)
            {
                try
                {
                    if (File.Exists(effectiveIndexPath))
                    {
                        File.Delete(effectiveIndexPath);
                        if (verbose) Console.Error.WriteLine("[verbose] cleared cache file");
                    }
                }
                catch (Exception dx) { if (verbose) Console.Error.WriteLine($"[warn] clear-cache failed: {dx.Message}"); }
            }
            indexFile = indexStore.Load();
            try
            {
                if (indexStore.Prune(indexFile, DateTimeOffset.UtcNow))
                {
                    if (verbose) Console.Error.WriteLine("[verbose] pruned legacy/invalid cache records");
                    indexStore.Save(indexFile);
                }
            }
            catch (Exception px) { if (verbose) Console.Error.WriteLine($"[warn] prune failed: {px.Message}"); }
            if (!refreshIndex && indexStore.TryGet(indexFile, compositeKey, out var rec) && rec != null)
            {
                var cachedHits = rec.Entries.Select(e => new AppHit(e.Type, e.Scope, e.Path, e.Version, e.PackageType, e.Source, e.Confidence, null)).ToList();
                // Existence validation on cached hits; remove stale entries and persist sanitized record.
                int beforeExist = cachedHits.Count;
                cachedHits = cachedHits.Where(h => SafePathExists(h.Path)).ToList();
                int removedExist = beforeExist - cachedHits.Count;
                if (removedExist > 0 && verbose)
                {
                    Console.Error.WriteLine($"[verbose] cache sanitized: removed {removedExist} non-existent paths");
                }
                if (removedExist > 0 && rec.Entries.Count > 0)
                {
                    // mutate underlying record entries to reflect removals and persist (best-effort)
                    var existingSet = new HashSet<string>(cachedHits.Select(h => h.Path), StringComparer.OrdinalIgnoreCase);
                    for (int i = rec.Entries.Count - 1; i >= 0; i--)
                    {
                        if (!existingSet.Contains(rec.Entries[i].Path)) rec.Entries.RemoveAt(i);
                    }
                    try { indexStore.Save(indexFile); } catch { }
                }
                // If all cached hits vanished, treat as cache miss (allow rebuild); do not short-circuit.
                if (cachedHits.Count == 0 && beforeExist > 0)
                {
                    if (verbose) Console.Error.WriteLine("[verbose] cache stale: all paths missing; bypassing short-circuit");
                }
                else
                {
                var working = cachedHits.Where(h => h.Confidence >= confidenceMin).OrderByDescending(h => h.Confidence).ToList();
                // Respect type filters on cached path
                if (onlyExe || onlyInstall || onlyConfig || onlyData)
                {
                    var allow = new HashSet<HitType>();
                    if (onlyExe) allow.Add(HitType.Exe);
                    if (onlyInstall) allow.Add(HitType.InstallDir);
                    if (onlyConfig) allow.Add(HitType.Config);
                    if (onlyData) allow.Add(HitType.Data);
                    working = working.Where(h => allow.Contains(h.Type)).ToList();
                }
                if (!all)
                {
                    var best = new Dictionary<HitType, AppHit>();
                    foreach (var h in working)
                    {
                        if (!best.TryGetValue(h.Type, out var exist)) { best[h.Type] = h; continue; }
                        if (h.Confidence > exist.Confidence + 1e-9) best[h.Type] = h;
                        else if (Math.Abs(h.Confidence - exist.Confidence) < 1e-9)
                        {
                            if (exist.Scope != Scope.Machine && h.Scope == Scope.Machine) best[h.Type] = h;
                            else if (h.Source.Length > exist.Source.Length) best[h.Type] = h;
                        }
                    }
                    working = best.Values.OrderByDescending(h => h.Confidence).ThenBy(h => h.Type.ToString()).ToList();
                }
                if (limit.HasValue) working = working.Take(limit.Value).ToList();
                if (verbose)
                {
                    Console.Error.WriteLine($"[verbose] cache-hit entries={cachedHits.Count} after-filters={working.Count} types={string.Join(',', working.GroupBy(h=>h.Type).Select(g=>$"{g.Key}={g.Count()}"))}");
                }
                if (working.Count > 0)
                {
                    EmitResults(working, json, csv, text, noColor, showPackageSources);
                    servedFromCache = true;
                    return 0;
                }
                if (rec.Entries.Count == 0)
                {
                    if (verbose) Console.Error.WriteLine("[info] cache short-circuit: known empty result set");
                    return 1; // no matches (cached)
                }
                }
            }
        }
        catch (Exception ex) { if (verbose) Console.Error.WriteLine($"[warn] index load failed: {ex.Message}"); }

    var hits = new List<AppHit>();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        // Parallel source execution with bounded degree
    var registry = BuildRegistry();
    var activeSources = registry.GetSources().Where(s => !(s is ProcessSource) || running).ToList();
    var traceRecords = trace ? new System.Collections.Concurrent.ConcurrentBag<(string name,int count,long ms,bool error)>() : null;
        int maxDegree = threads ?? Math.Min(Environment.ProcessorCount, 16);
        if (maxDegree < 1) maxDegree = 1;
        var sem = new SemaphoreSlim(maxDegree, maxDegree);
        var tasks = new List<Task>();
        foreach (var source in activeSources)
        {
            await sem.WaitAsync(cts.Token);
            tasks.Add(Task.Run(async () =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int localCount = 0; bool error = false;
                try
                {
                    var srcCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    srcCts.CancelAfter(options.Timeout);
                    await foreach (var hit in source.QueryAsync(normalized, options, srcCts.Token))
                    {
                        lock (hits) hits.Add(hit); localCount++;
                    }
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
                catch (Exception ex) { error = true; if (verbose) Console.Error.WriteLine($"[warn] {source.Name} failed: {ex.Message}"); }
                finally
                {
                    sw.Stop();
                    if (traceRecords != null) traceRecords.Add((source.Name, localCount, sw.ElapsedMilliseconds, error));
                }
                sem.Release();
            }, cts.Token));
        }
        try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { }
        if (traceRecords != null)
        {
            var totalMs = traceRecords.Sum(r => r.ms);
            foreach (var rec in traceRecords.OrderByDescending(r => r.ms))
            {
                Console.Error.WriteLine($"[trace] {rec.name,-22} {rec.ms,5} ms  hits={rec.count} {(rec.error ? "(error)" : string.Empty)}");
            }
            Console.Error.WriteLine($"[trace] total-sources-ms={totalMs}");
        }

        // PID-targeted enrichment (direct, bypassing name match) – adds process exe & its directory (if exists)
        if (pid.HasValue)
        {
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById(pid.Value);
                string? procPath = null;
                try { procPath = proc.MainModule?.FileName; } catch { }
                if (!string.IsNullOrWhiteSpace(procPath) && File.Exists(procPath))
                {
                    var ev = evidence ? new Dictionary<string,string>{{"ProcessId", pid.Value.ToString()},{"ProcessName", proc.ProcessName}} : null;
                    hits.Add(new AppHit(HitType.Exe, Scope.Machine, procPath, null, PackageType.Unknown, new[]{"Process"}, 0, ev));
                    var dir = Path.GetDirectoryName(procPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        hits.Add(new AppHit(HitType.InstallDir, Scope.Machine, dir!, null, PackageType.Unknown, new[]{"Process"}, 0, ev));
                }
            }
            catch (Exception ex) { if (verbose) Console.Error.WriteLine($"[warn] pid lookup failed: {ex.Message}"); }
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

        // De-duplicate & merge evidence/sources by (Type,Scope,NormalizedPath) case-insensitive path key.
        static string NormalizePath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return p;
            try
            {
                // GetFullPath handles relative segments; replace forward slashes for consistency.
                var full = Path.GetFullPath(p).TrimEnd();
                full = full.Replace('/', Path.DirectorySeparatorChar);
                // Trim trailing directory separator (except root like C:\)
                if (full.Length > 3 && (full.EndsWith("\\") || full.EndsWith("/")))
                    full = full.TrimEnd('\\','/');
                return full;
            }
            catch { return p.Replace('/', Path.DirectorySeparatorChar); }
        }
    // Existence filtering (drop any hits whose file/dir no longer exists) BEFORE merge/ranking to avoid noise.
    int preExistCount = hits.Count;
    hits = hits.Where(h => SafePathExists(h.Path)).ToList();
    int removed = preExistCount - hits.Count;
    if (removed > 0 && verbose) { Console.Error.WriteLine($"[verbose] filtered {removed} non-existent paths (pre-merge)"); }

    var mergedMap = new Dictionary<string, AppHit>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in hits)
        {
            var normPath = NormalizePath(h.Path);
            var key = $"{h.Type}|{h.Scope}|{normPath}";
            if (!mergedMap.TryGetValue(key, out var existing))
            {
                mergedMap[key] = h with { Path = normPath, Source = (string[])h.Source.Clone() };
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
        // Type filtering if any explicit type flags specified
        if (onlyExe || onlyInstall || onlyConfig || onlyData)
        {
            var allow = new HashSet<HitType>();
            if (onlyExe) allow.Add(HitType.Exe);
            if (onlyInstall) allow.Add(HitType.InstallDir);
            if (onlyConfig) allow.Add(HitType.Config);
            if (onlyData) allow.Add(HitType.Data);
            filtered = filtered.Where(h => allow.Contains(h.Type)).ToList();
        }
        if (!all)
        {
            // Collapse to best per HitType (keeping highest confidence). If multiple scopes for same type, prefer machine over user if confidence ties.
            var bestMap = new Dictionary<HitType, AppHit>();
            foreach (var h in filtered)
            {
                if (!bestMap.TryGetValue(h.Type, out var existing))
                {
                    bestMap[h.Type] = h;
                    continue;
                }
                bool replace = false;
                if (h.Confidence > existing.Confidence + 1e-9) replace = true; // strictly higher
                else if (Math.Abs(h.Confidence - existing.Confidence) < 1e-9)
                {
                    // Tie-break: prefer machine scope over user; else prefer one with more sources (evidence synergy)
                    if (existing.Scope != Scope.Machine && h.Scope == Scope.Machine) replace = true;
                    else if (h.Source.Length > existing.Source.Length) replace = true;
                }
                if (replace) bestMap[h.Type] = h;
            }
            filtered = bestMap.Values.OrderByDescending(h => h.Confidence).ThenBy(h => h.Type.ToString()).ToList();
        }
        if (limit.HasValue) filtered = filtered.Take(limit.Value).ToList();

        // Final enforcement pass for explicit type filters (guards against any later additions before emit)
        if (onlyExe || onlyInstall || onlyConfig || onlyData)
        {
            var allow = new HashSet<HitType>();
            if (onlyExe) allow.Add(HitType.Exe);
            if (onlyInstall) allow.Add(HitType.InstallDir);
            if (onlyConfig) allow.Add(HitType.Config);
            if (onlyData) allow.Add(HitType.Data);
            filtered = filtered.Where(h => allow.Contains(h.Type)).ToList();
        }
        // Post-filter consolidation: guard against any residual duplicates (e.g., cache merge anomalies or
        // future rule expansions adding duplicate config/data entries). This groups by (Type,Scope,Path) after
        // ranking & primary filtering but before evidence key filtering / ordering.
        if (filtered.Count > 1)
        {
            var finalMap = new Dictionary<string, AppHit>(StringComparer.OrdinalIgnoreCase);
            int dupCollapsed = 0;
            foreach (var h in filtered)
            {
                var key = $"{h.Type}|{h.Scope}|{h.Path}"; // path already normalized earlier
                if (!finalMap.TryGetValue(key, out var exist))
                {
                    finalMap[key] = h;
                    continue;
                }
                dupCollapsed++;
                // Merge sources (distinct)
                var srcSet = new HashSet<string>(exist.Source, StringComparer.OrdinalIgnoreCase);
                foreach (var s in h.Source) srcSet.Add(s);
                // Merge evidence (union, append distinct values pipe-separated)
                Dictionary<string,string>? evidenceMerged = null;
                if (exist.Evidence != null || h.Evidence != null)
                {
                    evidenceMerged = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    if (exist.Evidence != null)
                        foreach (var kv in exist.Evidence) evidenceMerged[kv.Key] = kv.Value;
                    if (h.Evidence != null)
                    {
                        foreach (var kv in h.Evidence)
                        {
                            if (evidenceMerged.TryGetValue(kv.Key, out var existingVal))
                            {
                                if (existingVal.Equals(kv.Value, StringComparison.OrdinalIgnoreCase)) continue;
                                var parts = existingVal.Split('|');
                                if (!parts.Any(p => p.Equals(kv.Value, StringComparison.OrdinalIgnoreCase)))
                                    evidenceMerged[kv.Key] = existingVal + "|" + kv.Value;
                            }
                            else
                            {
                                evidenceMerged[kv.Key] = kv.Value;
                            }
                        }
                    }
                }
                // Choose higher confidence (they should typically be equal); tie-break by: machine scope already same, then more sources.
                var chosen = h.Confidence > exist.Confidence + 1e-9 ? h : exist;
                if (Math.Abs(h.Confidence - exist.Confidence) < 1e-9 && h.Source.Length > exist.Source.Length) chosen = h;
                finalMap[key] = chosen with { Source = srcSet.ToArray(), Evidence = evidenceMerged };
            }
            if (dupCollapsed > 0)
            {
                filtered = finalMap.Values.OrderByDescending(h => h.Confidence).ThenBy(h => h.Type.ToString()).ToList();
                if (verbose)
                {
                    try { Console.Error.WriteLine($"[verbose] post-filter dedup collapsed {dupCollapsed} duplicate entries"); } catch { }
                }
            }
        }

        if (filtered.Count == 0)
        {
            // Persist an empty record to establish index presence for the query if not cached already.
            try
            {
                if (!servedFromCache && indexStore != null && indexFile != null)
                {
                    indexStore.Upsert(indexFile, compositeKey, Array.Empty<AppHit>(), DateTimeOffset.UtcNow);
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
    if (verbose)
        {
            try
            {
                var typeCounts = filtered.GroupBy(h => h.Type).Select(g => $"{g.Key}={g.Count()}");
        Console.Error.WriteLine($"[verbose] pre-emit counts: {string.Join(",", typeCounts)} showPackageSources={showPackageSources}");
                if (filtered.Count > 0)
                {
                    var sample = string.Join(" | ", filtered.Take(5).Select(h => h.Type+":"+System.IO.Path.GetFileName(h.Path)));
                    Console.Error.WriteLine($"[verbose] sample: {sample}");
                }
        Console.Error.WriteLine("[verbose] marker-before-emit");
            }
            catch { }
        }
        // Evidence filtering & deterministic ordering
        if (evidence)
        {
            for (int i = 0; i < filtered.Count; i++)
            {
                var ev = filtered[i].Evidence;
                if (ev == null) continue;
                // Filter if key list provided
                if (evidenceKeyFilter != null)
                {
                    var toRemove = ev.Keys.Where(k => !evidenceKeyFilter.Contains(k)).ToList();
                    foreach (var k in toRemove) ev.Remove(k);
                }
                if (ev.Count == 0)
                {
                    filtered[i] = filtered[i] with { Evidence = null };
                    continue;
                }
                // Deterministic ordering: rebuild dictionary with keys sorted ascending
                var ordered = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in ev.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)) ordered[k] = ev[k];
                filtered[i] = filtered[i] with { Evidence = ordered };
            }
        }
        else
        {
            // Ensure evidence suppressed when not requested
            for (int i = 0; i < filtered.Count; i++)
                if (filtered[i].Evidence != null)
                    filtered[i] = filtered[i] with { Evidence = null };
        }

        EmitResults(filtered, json, csv, text, noColor, showPackageSources);
    if (!servedFromCache && indexStore != null && indexFile != null)
        {
            try
            {
        indexStore.Upsert(indexFile, compositeKey, filtered, DateTimeOffset.UtcNow);
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

    private static string BuildCompositeKey(string normalizedQuery, bool user, bool machine, bool strict, bool running, int? pid, bool onlyExe, bool onlyInstall, bool onlyConfig, bool onlyData, double confidenceMin)
    {
        // Round confidenceMin to 2 decimals to avoid key explosion for tiny float differences
        var conf = Math.Round(confidenceMin, 2, MidpointRounding.AwayFromZero);
        return string.Join('|', new[]{
            normalizedQuery,
            user ? "u1" : "u0",
            machine ? "m1" : "m0",
            strict ? "s1" : "s0",
            running ? "r1" : "r0",
            pid.HasValue ? ("p"+pid.Value) : "p0",
            onlyExe ? "te" : "te0",
            onlyInstall ? "ti" : "ti0",
            onlyConfig ? "tc" : "tc0",
            onlyData ? "td" : "td0",
            $"c{conf:0.00}"
        });
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

    private static void EmitResults(List<AppHit> filtered, bool json, bool csv, bool text, bool noColor, bool showPackageSources = false)
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
            if (showPackageSources)
            {
                Console.Out.WriteLine("Type,Scope,Path,Version,PackageType,Sources,Confidence");
                foreach (var h in filtered)
                {
                    var src = string.Join('|', h.Source);
                    Console.Out.WriteLine($"{h.Type},{h.Scope},\"{h.Path}\",{h.Version},{h.PackageType},\"{src}\",{h.Confidence:0.###}");
                }
            }
            else
            {
                Console.Out.WriteLine("Type,Scope,Path,Version,PackageType,Confidence");
                foreach (var h in filtered)
                    Console.Out.WriteLine($"{h.Type},{h.Scope},\"{h.Path}\",{h.Version},{h.PackageType},{h.Confidence:0.###}");
            }
            return;
        }
        if (text)
        {
            foreach (var h in filtered)
            {
                if (noColor)
                {
                    if (showPackageSources)
                    {
                        Console.Out.WriteLine($"[{h.Confidence:0.00}] {h.Type} {h.Path} (pkg={h.PackageType}; src={string.Join('+', h.Source)})");
                    }
                    else
                    {
                        Console.Out.WriteLine($"[{h.Confidence:0.00}] {h.Type} {h.Path}");
                    }
                    continue;
                }
                var prevColor = Console.ForegroundColor;
                Console.ForegroundColor = ConfidenceColor(h.Confidence);
                Console.Out.Write($"[{h.Confidence:0.00}]");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Out.Write($" {h.Type}");
                Console.ForegroundColor = prevColor;
                if (showPackageSources)
                {
                    Console.Out.WriteLine($" {h.Path} (pkg={h.PackageType}; src={string.Join('+', h.Source)})");
                }
                else
                {
                    Console.Out.WriteLine($" {h.Path}");
                }
            }
        }
    }

    private static bool SafePathExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch { return false; }
    }
}
