using System.CommandLine;
using AppLocate.Core.Models;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Sources;
using AppLocate.Core.Ranking;
using AppLocate.Core.Rules;
// (Indexing removed)

namespace AppLocate.Cli {
    public static class Program {
        // Static reusable source arrays (CA1861 mitigation) centralized at class scope
        private static class SourceArrays {
            public static readonly string[] Process = ["Process"];
            public static readonly string[] Rules = ["Rules"];
        }
        private static ISourceRegistry BuildRegistry() {
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

        internal static async Task<int> RunAsync(string[] args) {
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
            var scoreBreakdownOpt = new Option<bool>("--score-breakdown") { Description = "Include per-hit score component breakdown (JSON adds 'breakdown', text shows extra lines)" };
            var timeoutOpt = new Option<int>("--timeout") { Description = "Per-source timeout seconds (default 5)" };
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
                limitOpt, confMinOpt, timeoutOpt, evidenceOpt, evidenceKeysOpt, scoreBreakdownOpt, verboseOpt, noColorOpt
            };
            // Manual token extraction (robust multi-word + -- sentinel)
            var parse = root.Parse(args);
            var tokens = parse.Tokens;
            if (parse.Errors?.Count > 0) {
                Console.Error.WriteLine(string.Join(Environment.NewLine, parse.Errors.Select(e => e.Message)));
                return 2;
            }
            bool HasRaw(string flag) {
                return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
            }

            if (HasRaw("-h") || HasRaw("--help")) {
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
                                  "  (cache removed: future snapshot index may return)\n" +
                                  "  --evidence            Include evidence keys\n" +
                                  "  --evidence-keys <k1,k2>  Only include specified evidence keys (implies --evidence)\n" +
                                  "  --score-breakdown      Show internal scoring component contributions per result\n" +
                                  "  --verbose             Verbose diagnostics\n" +
                                  "  --no-color            Disable ANSI colors\n" +
                                  "  --                    Treat following tokens as literal query");
                return 0;
            }
            // If user provided -- sentinel, everything after is treated as query (including dashes)
            var sentinelIdx = Array.IndexOf(args, "--");
            string? query = null;
            if (sentinelIdx >= 0 && sentinelIdx < args.Length - 1) {
                query = string.Join(' ', args.Skip(sentinelIdx + 1));
            }
            else {
                // Collect argument tokens that are not option values
                var valueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "--limit","--confidence-min","--timeout" };
                var parts = new List<string>();
                for (var i = 0; i < tokens.Count; i++) {
                    var tk = tokens[i];
                    if (tk.Type != System.CommandLine.Parsing.TokenType.Argument) {
                        continue;
                    }

                    if (tk.Value.Length > 0 && tk.Value[0] == '-') {
                        continue; // flag itself
                    }
                    // Skip if this token is a value for the previous option that expects a value
                    if (i > 0 && valueOptions.Contains(tokens[i - 1].Value)) {
                        continue;
                    }
                    // Skip if this token is numeric and immediately preceded by a known numeric option name (already covered above but double-safe)
                    parts.Add(tk.Value);
                }
                if (parts.Count > 0) {
                    query = string.Join(' ', parts);
                }
                // Fallback: first non-dash arg
                query ??= args.FirstOrDefault(a => !(a.Length > 0 && a[0] == '-'));
            }
            if (string.IsNullOrWhiteSpace(query)) { Console.Error.WriteLine("Missing <query>. Usage: applocate <query> [options] <name>"); return 2; }

            bool Has(string flag) {
                return tokens.Any(t => string.Equals(t.Value, flag, StringComparison.OrdinalIgnoreCase));
            }

            var json = Has("--json");
            var csv = !json && Has("--csv");
            var text = !json && !csv; // default text
            var user = Has("--user");
            var machine = Has("--machine");
            var strict = Has("--strict");
            var all = Has("--all");
            var onlyExe = Has("--exe");
            var onlyInstall = Has("--install-dir");
            var onlyConfig = Has("--config");
            var onlyData = Has("--data");
            var running = Has("--running");
            var pid = IntAfter("--pid");
            if (pid.HasValue && pid.Value <= 0) {
                Console.Error.WriteLine("--pid must be > 0");
                return 2;
            }
            if (pid.HasValue) {
                running = true; // imply
            }

            var showPackageSources = Has("--package-source");
            var threads = IntAfter("--threads");
            var trace = Has("--trace");
            if (threads.HasValue) {
                if (threads.Value <= 0) { Console.Error.WriteLine("--threads must be > 0"); return 2; }
                if (threads.Value > 128) { Console.Error.WriteLine("--threads too large (max 128)"); return 2; }
            }
            var evidence = Has("--evidence");
            string? evidenceKeysRaw = null;
            for (var i = 0; i < tokens.Count - 1; i++) {
                if (string.Equals(tokens[i].Value, "--evidence-keys", StringComparison.OrdinalIgnoreCase)) {
                    var candidate = tokens[i + 1].Value;
                    if (!(candidate.Length > 0 && candidate[0] == '-')) {
                        evidenceKeysRaw = candidate; // raw CSV
                    }
                }
            }
            HashSet<string>? evidenceKeyFilter = null;
            if (!string.IsNullOrWhiteSpace(evidenceKeysRaw)) {
                evidence = true; // implicit enable
                evidenceKeyFilter = new HashSet<string>(evidenceKeysRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s), StringComparer.OrdinalIgnoreCase);
            }
            var verbose = Has("--verbose");
            var noColor = Has("--no-color");
            // (index/cache options removed)

            int? IntAfter(string name) {
                for (var i = 0; i < tokens.Count; i++) {
                    if (string.Equals(tokens[i].Value, name, StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count) {
                        if (int.TryParse(tokens[i + 1].Value, out var v)) {
                            return v;
                        }
                    }
                }
                return null;
            }
            double? DoubleAfter(string name) {
                for (var i = 0; i < tokens.Count; i++) {
                    if (string.Equals(tokens[i].Value, name, StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count) {
                        if (double.TryParse(tokens[i + 1].Value, out var v)) {
                            return v;
                        }
                    }
                }
                return null;
            }
            var limit = IntAfter("--limit");
            if (limit.HasValue && limit.Value < 0) {
                Console.Error.WriteLine("--limit must be >= 0");
                return 2;
            }
            var confidenceMin = DoubleAfter("--confidence-min") ?? 0;
            if (confidenceMin is < 0 or > 1) {
                Console.Error.WriteLine("--confidence-min must be between 0 and 1");
                return 2;
            }
            var timeout = IntAfter("--timeout") ?? 5;
            if (timeout <= 0) {
                Console.Error.WriteLine("--timeout must be > 0");
                return 2;
            }
            if (timeout > 300) {
                Console.Error.WriteLine("--timeout too large (max 300 seconds)");
                return 2;
            }
            if (!noColor && (Console.IsOutputRedirected || Console.IsErrorRedirected)) {
                noColor = true;
            }

            var scoreBreakdown = Has("--score-breakdown");
            return await ExecuteAsync(query, json, csv, text, user, machine, strict, all, onlyExe, onlyInstall, onlyConfig, onlyData, running, pid, showPackageSources, threads, limit, confidenceMin, timeout, evidence, evidenceKeyFilter, scoreBreakdown, verbose, trace, noColor);
        }

        private static async Task<int> ExecuteAsync(string query, bool json, bool csv, bool text, bool user, bool machine, bool strict, bool all, bool onlyExe, bool onlyInstall, bool onlyConfig, bool onlyData, bool running, int? pid, bool showPackageSources, int? threads, int? limit, double confidenceMin, int timeoutSeconds, bool evidence, HashSet<string>? evidenceKeyFilter, bool scoreBreakdown, bool verbose, bool trace, bool noColor) {
            if (string.IsNullOrWhiteSpace(query)) {
                return 2;
            }

            if (verbose) {
                try {
                    Console.Error.WriteLine($"[verbose] query='{query}' strict={strict} all={all} onlyExe={onlyExe} onlyInstall={onlyInstall} onlyConfig={onlyConfig} onlyData={onlyData} running={running} pid={pid?.ToString() ?? "-"} pkgSrc={showPackageSources} evidence={evidence} scoreBreakdown={scoreBreakdown} evidenceKeys={(evidenceKeyFilter == null ? "(all|none)" : string.Join(',', evidenceKeyFilter))} json={json} csv={csv} text={text} confMin={confidenceMin} limit={limit?.ToString() ?? "-"} threads={threads?.ToString() ?? "-"}");
                }
                catch { }
            }
            var options = new SourceOptions(user, machine, TimeSpan.FromSeconds(timeoutSeconds), strict, evidence);
            var normalized = Normalize(query);
            // (index/cache removed)

            var hits = new List<AppHit>();
            // (Removed local static arrays; using Program.SourceArrays instead)

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            // Parallel source execution with bounded degree
            var registry = BuildRegistry();
            var activeSources = registry.GetSources().Where(s => s is not ProcessSource || running).ToList();
            var traceRecords = trace ? new System.Collections.Concurrent.ConcurrentBag<(string name, int count, long ms, bool error)>() : null;
            var maxDegree = threads ?? Math.Min(Environment.ProcessorCount, 16);
            if (maxDegree < 1) {
                maxDegree = 1;
            }

            var sem = new SemaphoreSlim(maxDegree, maxDegree);
            var tasks = new List<Task>();
            foreach (var source in activeSources) {
                await sem.WaitAsync(cts.Token);
                tasks.Add(Task.Run(async () => {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var localCount = 0; var error = false;
                    try {
                        var srcCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        srcCts.CancelAfter(options.Timeout);
                        await foreach (var hit in source.QueryAsync(normalized, options, srcCts.Token)) {
                            lock (hits) {
                                hits.Add(hit);
                            }

                            localCount++;
                        }
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
                    catch (Exception ex) {
                        error = true; if (verbose) {
                            Console.Error.WriteLine($"[warn] {source.Name} failed: {ex.Message}");
                        }
                    }
                    finally {
                        sw.Stop();
                        traceRecords?.Add((source.Name, localCount, sw.ElapsedMilliseconds, error));
                    }
                    _ = sem.Release();
                }, cts.Token));
            }
            try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { }
            if (traceRecords != null) {
                var totalMs = traceRecords.Sum(r => r.ms);
                foreach (var (name, count, ms, error) in traceRecords.OrderByDescending(r => r.ms)) {
                    Console.Error.WriteLine($"[trace] {name,-22} {ms,5} ms  hits={count} {(error ? "(error)" : string.Empty)}");
                }
                Console.Error.WriteLine($"[trace] total-sources-ms={totalMs}");
            }

            // PID-targeted enrichment (direct, bypassing name match) – adds process exe & its directory (if exists)
            if (pid.HasValue) {
                try {
                    var proc = System.Diagnostics.Process.GetProcessById(pid.Value);
                    string? procPath = null;
                    try { procPath = proc.MainModule?.FileName; } catch { }
                    if (!string.IsNullOrWhiteSpace(procPath) && File.Exists(procPath)) {
                        var ev = evidence ? new Dictionary<string, string> { { "ProcessId", pid.Value.ToString() }, { "ProcessName", proc.ProcessName } } : null;
                        hits.Add(new AppHit(HitType.Exe, Scope.Machine, procPath, null, PackageType.Unknown, SourceArrays.Process, 0, ev));
                        var dir = Path.GetDirectoryName(procPath);
                        if (!string.IsNullOrWhiteSpace(dir)) {
                            hits.Add(new AppHit(HitType.InstallDir, Scope.Machine, dir!, null, PackageType.Unknown, SourceArrays.Process, 0, ev));
                        }
                    }
                }
                catch (Exception ex) {
                    if (verbose) {
                        Console.Error.WriteLine($"[warn] pid lookup failed: {ex.Message}");
                    }
                }
            }

            // Rule-based expansion (config/data heuristics)
            try {
                var rulesPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "rules", "apps.default.yaml");
                if (!File.Exists(rulesPath)) {
                    // fallback to relative from current working dir
                    var alt = Path.Combine(Directory.GetCurrentDirectory(), "rules", "apps.default.yaml");
                    rulesPath = File.Exists(alt) ? alt : string.Empty;
                }
                if (!string.IsNullOrEmpty(rulesPath)) {
                    var loaded = await RulesEngine.LoadAsync(rulesPath, CancellationToken.None);
                    if (loaded.Count > 0) {
                        // Determine if any rule matches query tokens or existing exe/install hits
                        var allNames = hits.Select(h => Path.GetFileNameWithoutExtension(h.Path)?.ToLowerInvariant()).Where(s => !string.IsNullOrEmpty(s)).ToHashSet();
                        foreach (var rule in loaded) {
                            var match = rule.MatchAnyOf.Any(m =>
                                string.Equals(m, query, StringComparison.OrdinalIgnoreCase) ||
                                allNames.Contains(m.ToLowerInvariant()));
                            if (!match) {
                                continue;
                            }

                            foreach (var cfg in rule.Config) {
                                var expanded = Environment.ExpandEnvironmentVariables(cfg.Replace('/', Path.DirectorySeparatorChar));
                                hits.Add(new AppHit(HitType.Config, Scope.User, expanded, null, PackageType.Unknown, SourceArrays.Rules, 0, new Dictionary<string, string> { { "Rule", "config" } }));
                            }
                            foreach (var dat in rule.Data) {
                                var expanded = Environment.ExpandEnvironmentVariables(dat.Replace('/', Path.DirectorySeparatorChar));
                                hits.Add(new AppHit(HitType.Data, Scope.User, expanded, null, PackageType.Unknown, SourceArrays.Rules, 0, new Dictionary<string, string> { { "Rule", "data" } }));
                            }
                        }
                    }
                }
            }
            catch (Exception rex) {
                if (verbose) {
                    Console.Error.WriteLine($"[warn] rules expansion failed: {rex.Message}");
                }
            }

            // De-duplicate & merge evidence/sources by (Type,Scope,NormalizedPath) case-insensitive path key.
            static string NormalizePath(string p) {
                if (string.IsNullOrWhiteSpace(p)) {
                    return p;
                }

                try {
                    // GetFullPath handles relative segments; replace forward slashes for consistency.
                    var full = Path.GetFullPath(p).TrimEnd();
                    full = full.Replace('/', Path.DirectorySeparatorChar);
                    // Trim trailing directory separator (except root like C:\)
                    if (full.Length > 3 && (full.EndsWith('\\') || full.EndsWith('/'))) {
                        full = full.TrimEnd('\\', '/');
                    }

                    return full;
                }
                catch { return p.Replace('/', Path.DirectorySeparatorChar); }
            }
            // Existence filtering (drop any hits whose file/dir no longer exists) BEFORE merge/ranking to avoid noise.
            var preExistCount = hits.Count;
            hits = [.. hits.Where(h => SafePathExists(h.Path))];
            var removed = preExistCount - hits.Count;
            if (removed > 0 && verbose) { Console.Error.WriteLine($"[verbose] filtered {removed} non-existent paths (pre-merge)"); }

            // Unified scope normalization & early --user/--machine pruning
            if (user || machine) {
                var before = hits.Count;
                hits = NormalizeAndFilterScopes(hits, user, machine);
                if (verbose) {
                    try { Console.Error.WriteLine($"[verbose] scope filter applied ({before}->{hits.Count}) userOnly={user} machineOnly={machine}"); } catch { }
                }
            }
            else {
                hits = NormalizeAndFilterScopes(hits, false, false);
            }

            var mergedMap = new Dictionary<string, AppHit>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in hits) {
                var normPath = NormalizePath(h.Path);
                var key = $"{h.Type}|{h.Scope}|{normPath}";
                if (!mergedMap.TryGetValue(key, out var existing)) {
                    mergedMap[key] = h with { Path = normPath, Source = (string[])h.Source.Clone() };
                    continue;
                }
                // Merge: combine unique sources; merge evidence keys by accumulating distinct values.
                var srcSet = new HashSet<string>(existing.Source, StringComparer.OrdinalIgnoreCase);
                foreach (var s in h.Source) {
                    _ = srcSet.Add(s);
                }

                Dictionary<string, string>? evidenceMerged = null;
                if (existing.Evidence != null || h.Evidence != null) {
                    evidenceMerged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (existing.Evidence != null) {
                        foreach (var kv in existing.Evidence) {
                            evidenceMerged[kv.Key] = kv.Value;
                        }
                    }
                    if (h.Evidence != null) {
                        foreach (var kv in h.Evidence) {
                            if (evidenceMerged.TryGetValue(kv.Key, out var existingVal)) {
                                // If same value already present (case-insensitive compare), skip.
                                if (existingVal.Equals(kv.Value, StringComparison.OrdinalIgnoreCase)) {
                                    continue;
                                }
                                // If existing contains pipe-separated list, check for presence; else append.
                                var parts = existingVal.Split('|');
                                if (!parts.Any(p => p.Equals(kv.Value, StringComparison.OrdinalIgnoreCase))) {
                                    evidenceMerged[kv.Key] = existingVal + "|" + kv.Value;
                                }
                            }
                            else {
                                evidenceMerged[kv.Key] = kv.Value;
                            }
                        }
                    }
                }
                // Keep existing for now; will rescore after loop.
                mergedMap[key] = existing with { Source = [.. srcSet], Evidence = evidenceMerged };
            }
            var scored = new List<AppHit>(mergedMap.Count);
            foreach (var h in mergedMap.Values) {
                if (scoreBreakdown) {
                    var (score, breakdown) = Ranker.ScoreWithBreakdown(normalized, h);
                    scored.Add(h with { Confidence = score, Breakdown = breakdown });
                }
                else {
                    scored.Add(h with { Confidence = Ranker.Score(normalized, h) });
                }
            }

            // Post-score path-level consolidation & install-dir pairing boost:
            // Rationale: Some environments surface the same path via multiple sources but (rarely) with scope variance
            // that slips past earlier (Type,Scope,Path) merge (e.g., Program Files path mis-attributed as user scope by a source).
            // We collapse duplicates ignoring scope (prefer Machine when any Machine scope present) and union sources/evidence.
            // Additionally, boost install directories that contain a high-confidence exe hit (primary pairing) so the directory
            // does not rank far below its exe counterpart and to avoid user confusion about relative ordering.
            if (scored.Count > 1) {
                // Map directory -> max exe confidence for pairing boost.
                var exeByDir = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in scored.Where(h => h.Type == HitType.Exe)) {
                    try {
                        var dir = Path.GetDirectoryName(e.Path);
                        if (string.IsNullOrEmpty(dir)) {
                            continue;
                        }

                        if (!exeByDir.TryGetValue(dir, out var existing) || e.Confidence > existing) {
                            exeByDir[dir] = e.Confidence;
                        }
                    }
                    catch { }
                }

                var pathCollapse = new Dictionary<string, AppHit>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in scored) {
                    var keyNoScope = $"{h.Type}|{NormalizePath(h.Path)}"; // ignore scope for consolidation
                    if (!pathCollapse.TryGetValue(keyNoScope, out var exist)) {
                        pathCollapse[keyNoScope] = h;
                        continue;
                    }
                    // Prefer machine scope if any disagreement; keep higher confidence else union sources
                    var chosen = exist;
                    if (exist.Scope != Scope.Machine && h.Scope == Scope.Machine) {
                        chosen = h;
                    }
                    else if (h.Confidence > exist.Confidence + 1e-9) {
                        chosen = h;
                    }
                    // Merge sources
                    var srcSet = new HashSet<string>(exist.Source, StringComparer.OrdinalIgnoreCase);
                    foreach (var s in h.Source) {
                        _ = srcSet.Add(s);
                    }
                    // Merge evidence dictionaries (pipe-append distinct values)
                    Dictionary<string, string>? mergedEv = null;
                    if (exist.Evidence != null || h.Evidence != null) {
                        mergedEv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        if (exist.Evidence != null) {
                            foreach (var kv in exist.Evidence) {
                                mergedEv[kv.Key] = kv.Value;
                            }
                        }

                        if (h.Evidence != null) {
                            foreach (var kv in h.Evidence) {
                                if (mergedEv.TryGetValue(kv.Key, out var val)) {
                                    var parts = val.Split('|');
                                    if (!parts.Any(p => p.Equals(kv.Value, StringComparison.OrdinalIgnoreCase))) {
                                        mergedEv[kv.Key] = val + "|" + kv.Value;
                                    }
                                }
                                else {
                                    mergedEv[kv.Key] = kv.Value;
                                }
                            }
                        }
                    }
                    // Annotate merge reason (for future --evidence visibility) without flooding existing keys.
                    mergedEv ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    mergedEv["PathMerged"] = "1";
                    pathCollapse[keyNoScope] = chosen with { Source = [.. srcSet], Evidence = mergedEv };
                }

                // Apply pairing boost: install dir gets +min(0.12, exeConf * 0.15) if associated exe has reasonably high confidence.
                // This occurs before later per-type collapse to influence ordering.
                var adjusted = new List<AppHit>(pathCollapse.Count);
                foreach (var h in pathCollapse.Values) {
                    if (h.Type == HitType.InstallDir) {
                        try {
                            if (exeByDir.TryGetValue(h.Path, out var exeConf) && exeConf >= 0.35) {
                                var boost = Math.Min(0.12, exeConf * 0.15); // scales with exe confidence
                                var newConf = h.Confidence + boost;
                                if (newConf > 1) {
                                    newConf = 1;
                                }
                                // add evidence marker
                                var ev2 = h.Evidence != null
                                    ? new Dictionary<string, string>(h.Evidence, StringComparer.OrdinalIgnoreCase)
                                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                ev2["ExePair"] = "1";
                                adjusted.Add(h with { Confidence = newConf, Evidence = ev2 });
                                continue;
                            }
                        }
                        catch { }
                    }
                    adjusted.Add(h);
                }
                scored = adjusted;
            }
            // Generic directory penalty (5): If a generic container directory (System32, */bin, mingw*/bin) appears as InstallDir and we already
            // have a higher-confidence exe in that directory, demote the directory's confidence to reduce noise.
            if (scored.Count > 1) {
                var exeByDir = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var ex in scored.Where(x => x.Type == HitType.Exe)) {
                    try {
                        var d = Path.GetDirectoryName(ex.Path);
                        if (string.IsNullOrEmpty(d)) {
                            continue;
                        }

                        if (!exeByDir.TryGetValue(d, out var cur) || ex.Confidence > cur) {
                            exeByDir[d] = ex.Confidence;
                        }
                    }
                    catch { }
                }
                for (var i = 0; i < scored.Count; i++) {
                    var h = scored[i];
                    if (h.Type != HitType.InstallDir) {
                        continue;
                    }

                    var p = h.Path.ToLowerInvariant();
                    var generic = p.EndsWith("\\system32", StringComparison.Ordinal)
                                    || p.EndsWith("/system32", StringComparison.Ordinal)
                                    || p.EndsWith("\\bin", StringComparison.Ordinal)
                                    || p.EndsWith("/bin", StringComparison.Ordinal);
                    if (!generic) {
                        continue;
                    }

                    if (exeByDir.TryGetValue(h.Path, out var exConf) && exConf >= h.Confidence) {
                        var newConf = Math.Max(0, h.Confidence - 0.30); // strong demotion
                        var ev = h.Evidence != null
                            ? new Dictionary<string, string>(h.Evidence, StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        ev["GenericDirPenalty"] = "1";
                        scored[i] = h with { Confidence = newConf, Evidence = ev };
                    }
                }
            }
            // Confidence floor for paired install dirs: avoid surfacing 0.00 for a directory that clearly hosts a high-confidence exe
            if (scored.Count > 0) {
                var exeByDir2 = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var ex in scored.Where(x => x.Type == HitType.Exe)) {
                    try {
                        var d = Path.GetDirectoryName(ex.Path);
                        if (string.IsNullOrEmpty(d)) {
                            continue;
                        }

                        if (!exeByDir2.TryGetValue(d, out var cur) || ex.Confidence > cur) {
                            exeByDir2[d] = ex.Confidence;
                        }
                    }
                    catch { }
                }
                for (var i = 0; i < scored.Count; i++) {
                    var h = scored[i];
                    if (h.Type != HitType.InstallDir) {
                        continue;
                    }

                    if (!exeByDir2.TryGetValue(h.Path, out var exConf) || exConf < 0.5) {
                        continue; // only if associated exe fairly strong
                    }

                    if (h.Confidence > 0.00001) {
                        continue; // already has non-zero
                    }
                    // Provide small floor so user sees a non-zero but still low value
                    var floor = 0.08;
                    var ev = h.Evidence != null ? new Dictionary<string, string>(h.Evidence, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    ev["DirMinFloor"] = floor.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    scored[i] = h with { Confidence = floor, Evidence = ev };
                }
            }
            // Orphan install dir enrichment: some package roots (e.g., Winget portable) store the real exe one level deeper
            // resulting in an install dir hit without a paired exe. We do a shallow (depth=2) probe for a single primary exe name
            // matching the query tokens to pair it, avoiding broad recursion.
            try {
                if (scored.Count > 0) {
                    // Build fast lookup of exe directories we already have
                    var existingExeDirs = new HashSet<string>(scored.Where(x => x.Type == HitType.Exe)
                                                                   .Select(x => Path.GetDirectoryName(x.Path)!)
                                                                   .Where(p => !string.IsNullOrEmpty(p)), StringComparer.OrdinalIgnoreCase);
                    // Candidate orphan roots: install dirs that have NO exe directly in same path and not generic system dirs
                    var orphanDirs = scored.Where(x => x.Type == HitType.InstallDir && !existingExeDirs.Contains(x.Path))
                                           .Take(12) // safety cap
                                           .ToList();
                    if (orphanDirs.Count > 0) {
                        var normTokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var od in orphanDirs) {
                            try {
                                // Quick directory existence & skip if huge (heuristic: >200 entries top-level)
                                if (!Directory.Exists(od.Path)) {
                                    continue;
                                }

                                var entryCount = 0;
                                IEnumerable<string> subDirs = [];
                                try {
                                    subDirs = Directory.EnumerateDirectories(od.Path, "*", SearchOption.TopDirectoryOnly);
                                    entryCount = subDirs.Take(201).Count();
                                    if (entryCount > 200) {
                                        continue; // too large, skip expensive scan
                                    }
                                }
                                catch { }
                                // Probe first-level subdirectories for a bin folder OR token-matching folder.
                                var primaryLeaf = subDirs.FirstOrDefault(d => Path.GetFileName(d).Equals("bin", StringComparison.OrdinalIgnoreCase));
                                primaryLeaf ??= subDirs.FirstOrDefault(d => {
                                    var leaf = Path.GetFileName(d).ToLowerInvariant();
                                    return normTokens.Length > 0 && normTokens.All(t => leaf.Contains(t));
                                });
                                string? depth2Root = null;
                                var depth2 = false;
                                // Depth=2 heuristic (Winget portable): look for a single *version* dir then bin beneath OR a single bin beneath any version-like dir.
                                if (primaryLeaf == null) {
                                    // Allow small fan-out (<=8) to avoid large scans
                                    var subDirArr = subDirs.Take(9).ToArray();
                                    if (subDirArr.Length <= 8) {
                                        // Prefer one directory whose name looks like version (contains digits or '-')
                                        var versionLike = subDirArr.Where(d => {
                                            var leaf = Path.GetFileName(d);
                                            return leaf.Any(char.IsDigit) || leaf.Contains('-');
                                        }).OrderBy(d => Path.GetFileName(d).Length).ToList();
                                        foreach (var candidate in versionLike) {
                                            IEnumerable<string> sub2 = [];
                                            try { sub2 = Directory.EnumerateDirectories(candidate, "*", SearchOption.TopDirectoryOnly); } catch { }
                                            var sub2Arr = sub2.Take(9).ToArray();
                                            // Look for bin
                                            var binDir = sub2Arr.FirstOrDefault(d => Path.GetFileName(d).Equals("bin", StringComparison.OrdinalIgnoreCase));
                                            if (binDir != null) {
                                                depth2Root = candidate;
                                                primaryLeaf = binDir;
                                                depth2 = true;
                                                break;
                                            }
                                            // Or a nested folder matching tokens
                                            var tokenDir = sub2Arr.FirstOrDefault(d => {
                                                var leaf = Path.GetFileName(d).ToLowerInvariant();
                                                return normTokens.Length > 0 && normTokens.All(t => leaf.Contains(t));
                                            });
                                            if (tokenDir != null) {
                                                depth2Root = candidate;
                                                primaryLeaf = tokenDir;
                                                depth2 = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                                if (primaryLeaf == null) {
                                    continue; // still nothing
                                }
                                // Enumerate executables in that leaf (top-level only)
                                IEnumerable<string> exes = [];
                                try { exes = Directory.EnumerateFiles(primaryLeaf, "*.exe", SearchOption.TopDirectoryOnly); } catch { }
                                string? chosen = null; var chosenScore = double.MinValue;
                                foreach (var exePath in exes) {
                                    var name = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
                                    // Score simple: token coverage count
                                    var tokenMatches = 0;
                                    foreach (var t in normTokens) {
                                        if (name.Contains(t)) {
                                            tokenMatches++;
                                        }
                                    }

                                    if (tokenMatches == 0) {
                                        continue;
                                    }

                                    var score = (double)tokenMatches / Math.Max(1, normTokens.Length);
                                    if (score > chosenScore) {
                                        chosenScore = score;
                                        chosen = exePath;
                                    }
                                }
                                if (chosen != null && File.Exists(chosen)) {
                                    // Add exe hit with conservative confidence baseline (rescored later).
                                    Dictionary<string, string>? ev = null;
                                    if (evidence) {
                                        ev = new(StringComparer.OrdinalIgnoreCase)
                                        {
                                            { depth2 ? "OrphanDirProbe2" : "OrphanDirProbe", od.Path }
                                        };
                                        if (depth2 && depth2Root != null) {
                                            ev["Depth2Root"] = depth2Root;
                                            ev["PrimaryLeaf"] = primaryLeaf;
                                        }
                                    }
                                    scored.Add(new AppHit(HitType.Exe, od.Scope, chosen, null, od.PackageType, ["OrphanProbe"], 0, ev));
                                    // If depth2 discovered a more accurate install root (version folder), surface it as InstallDir if not already present.
                                    if (depth2 && depth2Root != null && Directory.Exists(depth2Root) && !scored.Any(h => h.Type == HitType.InstallDir && string.Equals(h.Path, depth2Root, StringComparison.OrdinalIgnoreCase))) {
                                        Dictionary<string, string>? dirEv = null;
                                        if (evidence) {
                                            dirEv = new(StringComparer.OrdinalIgnoreCase)
                                            {
                                                { "DerivedInstallDir", "true" },
                                                { "FromDepth2", od.Path }
                                            };
                                        }
                                        scored.Add(new AppHit(HitType.InstallDir, od.Scope, depth2Root, null, od.PackageType, ["OrphanProbe"], 0, dirEv));
                                    }
                                }
                                else if (!depth2) {
                                    // Explicit 2-hop fallback: look for <orphan>/<versionLike>/(bin|token)/<queryLike>.exe pattern
                                    // Only attempt if fan-out small
                                    var subDirArr2 = subDirs.Take(9).ToArray();
                                    if (subDirArr2.Length <= 8) {
                                        foreach (var vdir in subDirArr2) {
                                            var leafName = Path.GetFileName(vdir);
                                            if (!(leafName.Any(char.IsDigit) || leafName.Contains('-'))) {
                                                continue; // version-like
                                            }

                                            IEnumerable<string> innerDirs = [];
                                            try { innerDirs = Directory.EnumerateDirectories(vdir, "*", SearchOption.TopDirectoryOnly); } catch { }
                                            var innerArr = innerDirs.Take(9).ToArray();
                                            foreach (var inner in innerArr) {
                                                var innerLeaf = Path.GetFileName(inner).ToLowerInvariant();
                                                if (!(innerLeaf == "bin" || (normTokens.Length > 0 && normTokens.All(innerLeaf.Contains)))) {
                                                    continue;
                                                }

                                                IEnumerable<string> innerExes = [];
                                                try { innerExes = Directory.EnumerateFiles(inner, "*.exe", SearchOption.TopDirectoryOnly); } catch { }
                                                foreach (var exePath in innerExes) {
                                                    var name = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
                                                    if (!name.Contains(normalized)) {
                                                        continue;
                                                    }

                                                    if (!File.Exists(exePath)) {
                                                        continue;
                                                    }

                                                    Dictionary<string, string>? ev2 = null;
                                                    if (evidence) {
                                                        ev2 = new(StringComparer.OrdinalIgnoreCase)
                                                        {
                                                            { "OrphanDirProbe2Pattern", od.Path },
                                                            { "VersionDir", vdir },
                                                            { "Leaf", inner }
                                                        };
                                                    }
                                                    scored.Add(new AppHit(HitType.Exe, od.Scope, exePath, null, od.PackageType, ["OrphanProbe"], 0, ev2));
                                                    if (!scored.Any(h => h.Type == HitType.InstallDir && string.Equals(h.Path, vdir, StringComparison.OrdinalIgnoreCase))) {
                                                        Dictionary<string, string>? dirEv2 = null;
                                                        if (evidence) {
                                                            dirEv2 = new(StringComparer.OrdinalIgnoreCase)
                                                            {
                                                                { "DerivedInstallDir", "true" },
                                                                { "FromPatternDepth2", od.Path }
                                                            };
                                                        }
                                                        scored.Add(new AppHit(HitType.InstallDir, od.Scope, vdir, null, od.PackageType, ["OrphanProbe"], 0, dirEv2));
                                                    }
                                                    goto DonePattern; // only take first matching pattern
                                                }
                                            }
                                        }
                                    }
                                }
                            DonePattern:;
                            }
                            catch { }
                        }
                        // Re-score only newly added orphan exe hits
                        for (var i = 0; i < scored.Count; i++) {
                            var h = scored[i];
                            if (h.Type == HitType.Exe && h.Confidence == 0 && h.Source.Length == 1 && h.Source[0] == "OrphanProbe") {
                                if (scoreBreakdown) {
                                    var (s, b) = Ranker.ScoreWithBreakdown(normalized, h);
                                    scored[i] = h with { Confidence = s, Breakdown = b };
                                }
                                else {
                                    scored[i] = h with { Confidence = Ranker.Score(normalized, h) };
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            var filtered = scored.Where(h => h.Confidence >= confidenceMin).OrderByDescending(h => h.Confidence).ToList();
            // Type filtering if any explicit type flags specified
            if (onlyExe || onlyInstall || onlyConfig || onlyData) {
                var allow = new HashSet<HitType>();
                if (onlyExe) {
                    _ = allow.Add(HitType.Exe);
                }

                if (onlyInstall) {
                    _ = allow.Add(HitType.InstallDir);
                }

                if (onlyConfig) {
                    _ = allow.Add(HitType.Config);
                }

                if (onlyData) {
                    _ = allow.Add(HitType.Data);
                }

                filtered = [.. filtered.Where(h => allow.Contains(h.Type))];
            }
            if (!all) {
                // Enhanced collapse strategy:
                //  * Always surface top exe plus additional high-confidence exe(s) from distinct directories (pair mode)
                //  * For each surfaced exe ensure its install directory is included (create synthetic if necessary)
                //  * Preserve multi-version install dir family (variant siblings) for the top install dir (FL Studio case)
                //  * Drop orphan install dirs that are not paired with any selected exe nor variant sibling
                var selected = new List<AppHit>();

                // ----- Phase 1: Exe pairing -----
                var exeGroups = filtered.Where(h => h.Type == HitType.Exe)
                                         .OrderByDescending(h => h.Confidence)
                                         .ToList();
                var selectedExeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var topExeConf = exeGroups.Count > 0 ? exeGroups[0].Confidence : 0;
                var exePairCap = 3; // safety cap to avoid flooding output
                for (var i = 0; i < exeGroups.Count && selectedExeDirs.Count < exePairCap; i++) {
                    var ex = exeGroups[i];
                    var dir = Path.GetDirectoryName(ex.Path);
                    if (string.IsNullOrEmpty(dir)) {
                        continue;
                    }

                    if (selectedExeDirs.Contains(dir)) {
                        continue;
                    }
                    // Inclusion criteria: always take first; subsequent need to be within delta or above absolute threshold
                    if (i == 0 || ex.Confidence >= topExeConf - 0.11 || ex.Confidence >= 0.60) {
                        selected.Add(ex);
                        _ = selectedExeDirs.Add(dir);
                    }
                }

                // ----- Phase 2: Ensure install dir for each selected exe -----
                var installLookup = filtered.Where(h => h.Type == HitType.InstallDir)
                                            .ToDictionary(h => h.Path, h => h, StringComparer.OrdinalIgnoreCase);
                foreach (var dir in selectedExeDirs) {
                    if (installLookup.TryGetValue(dir, out var existingDirHit)) {
                        if (!selected.Any(h => h.Type == HitType.InstallDir && string.Equals(h.Path, dir, StringComparison.OrdinalIgnoreCase))) {
                            selected.Add(existingDirHit);
                        }
                    }
                    else {
                        // Synthetic directory hit (rare). Clone minimal evidence from its exe.
                        var exeRef = selected.First(e => e.Type == HitType.Exe && Path.GetDirectoryName(e.Path) == dir);
                        Dictionary<string, string>? ev = null;
                        if (evidence && exeRef.Evidence != null) {
                            ev = new Dictionary<string, string>(exeRef.Evidence, StringComparer.OrdinalIgnoreCase) {
                                ["AutoPair"] = "1"
                            };
                        }
                        else if (evidence) {
                            ev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "AutoPair", "1" } };
                        }
                        selected.Add(new AppHit(HitType.InstallDir, exeRef.Scope, dir!, exeRef.Version, exeRef.PackageType, exeRef.Source, Math.Min(exeRef.Confidence, 0.50), ev));
                    }
                }

                // ----- Phase 3: Variant sibling expansion for primary install dir family (reuse previous heuristic) -----
                static bool SameVariantFamily(string a, string b) {
                    try {
                        var nameA = Path.GetFileName(a).ToLowerInvariant();
                        var nameB = Path.GetFileName(b).ToLowerInvariant();
                        var tokensA = nameA.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        var tokensB = nameB.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        return tokensA.Length < 2 || tokensB.Length < 2 ? false : tokensA[0] == tokensB[0] && tokensA[1] == tokensB[1];
                    }
                    catch { return false; }
                }
                // Determine primary install dir (highest confidence among already selected dir hits)
                var selectedInstallDirs = selected.Where(h => h.Type == HitType.InstallDir)
                                                  .OrderByDescending(h => h.Confidence)
                                                  .ToList();
                var primaryInstall = selectedInstallDirs.FirstOrDefault();
                if (primaryInstall != null) {
                    var allInstallDirs = filtered.Where(h => h.Type == HitType.InstallDir)
                                                 .OrderByDescending(h => h.Confidence)
                                                 .ToList();
                    var addedVariants = 0;
                    foreach (var cand in allInstallDirs) {
                        if (selected.Any(h => h.Type == HitType.InstallDir && string.Equals(h.Path, cand.Path, StringComparison.OrdinalIgnoreCase))) {
                            continue;
                        }

                        if (!SameVariantFamily(primaryInstall.Path, cand.Path)) {
                            continue;
                        }

                        if (cand.Confidence < primaryInstall.Confidence - 0.12) {
                            continue;
                        }

                        var ev = cand.Evidence != null ? new Dictionary<string, string>(cand.Evidence, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        ev["VariantSibling"] = primaryInstall.Path;
                        selected.Add(cand with { Evidence = ev });
                        addedVariants++;
                        if (addedVariants >= 3) {
                            break; // cap variant expansion
                        }
                    }
                }

                // ----- Phase 4: Single-best for remaining types (Config/Data/etc.) -----
                var handledTypes = new HashSet<HitType> { HitType.Exe, HitType.InstallDir };
                foreach (var typeGroup in filtered.Where(h => !handledTypes.Contains(h.Type)).GroupBy(h => h.Type)) {
                    AppHit? best = null;
                    foreach (var h in typeGroup) {
                        if (best == null) { best = h; continue; }
                        var current = best;
                        var replace = false;
                        if (h.Confidence > current.Confidence + 1e-9) {
                            replace = true;
                        }
                        else if (Math.Abs(h.Confidence - current.Confidence) < 1e-9) {
                            if (current.Scope != Scope.Machine && h.Scope == Scope.Machine) {
                                replace = true;
                            }
                            else if (h.Source.Length > current.Source.Length) {
                                replace = true;
                            }
                        }
                        if (replace) {
                            best = h;
                        }
                    }
                    if (best != null) {
                        selected.Add(best);
                    }
                }

                // ----- Phase 5: Remove orphan install dirs (those without a selected exe and not marked VariantSibling) -----
                var exeDirSet = selected.Where(h => h.Type == HitType.Exe)
                                        .Select(h => Path.GetDirectoryName(h.Path))
                                        .Where(p => !string.IsNullOrEmpty(p))
                                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                selected = [.. selected.Where(h => h.Type != HitType.InstallDir || exeDirSet.Contains(h.Path) || (h.Evidence != null && h.Evidence.ContainsKey("VariantSibling")))];

                filtered = [.. selected.OrderByDescending(h => h.Confidence).ThenBy(h => h.Type.ToString())];
            }
            if (limit.HasValue) {
                filtered = [.. filtered.Take(limit.Value)];
            }

            // Final enforcement pass for explicit type filters (guards against any later additions before emit)
            if (onlyExe || onlyInstall || onlyConfig || onlyData) {
                var allow = new HashSet<HitType>();
                if (onlyExe) {
                    _ = allow.Add(HitType.Exe);
                }

                if (onlyInstall) {
                    _ = allow.Add(HitType.InstallDir);
                }

                if (onlyConfig) {
                    _ = allow.Add(HitType.Config);
                }

                if (onlyData) {
                    _ = allow.Add(HitType.Data);
                }

                filtered = [.. filtered.Where(h => allow.Contains(h.Type))];
            }
            // Post-filter consolidation: guard against any residual duplicates (e.g., cache merge anomalies or
            // future rule expansions adding duplicate config/data entries). This groups by (Type,Scope,Path) after
            // ranking & primary filtering but before evidence key filtering / ordering.
            if (filtered.Count > 1) {
                var finalMap = new Dictionary<string, AppHit>(StringComparer.OrdinalIgnoreCase);
                var dupCollapsed = 0;
                foreach (var h in filtered) {
                    var key = $"{h.Type}|{h.Scope}|{h.Path}"; // path already normalized earlier
                    if (!finalMap.TryGetValue(key, out var exist)) {
                        finalMap[key] = h;
                        continue;
                    }
                    dupCollapsed++;
                    // Merge sources (distinct)
                    var srcSet = new HashSet<string>(exist.Source, StringComparer.OrdinalIgnoreCase);
                    foreach (var s in h.Source) {
                        _ = srcSet.Add(s);
                    }
                    // Merge evidence (union, append distinct values pipe-separated)
                    Dictionary<string, string>? evidenceMerged = null;
                    if (exist.Evidence != null || h.Evidence != null) {
                        evidenceMerged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        if (exist.Evidence != null) {
                            foreach (var kv in exist.Evidence) {
                                evidenceMerged[kv.Key] = kv.Value;
                            }
                        }

                        if (h.Evidence != null) {
                            foreach (var kv in h.Evidence) {
                                if (evidenceMerged.TryGetValue(kv.Key, out var existingVal)) {
                                    if (existingVal.Equals(kv.Value, StringComparison.OrdinalIgnoreCase)) {
                                        continue;
                                    }

                                    var parts = existingVal.Split('|');
                                    if (!parts.Any(p => p.Equals(kv.Value, StringComparison.OrdinalIgnoreCase))) {
                                        evidenceMerged[kv.Key] = existingVal + "|" + kv.Value;
                                    }
                                }
                                else {
                                    evidenceMerged[kv.Key] = kv.Value;
                                }
                            }
                        }
                    }
                    // Choose higher confidence (they should typically be equal); tie-break by: machine scope already same, then more sources.
                    var chosen = h.Confidence > exist.Confidence + 1e-9 ? h : exist;
                    if (Math.Abs(h.Confidence - exist.Confidence) < 1e-9 && h.Source.Length > exist.Source.Length) {
                        chosen = h;
                    }

                    finalMap[key] = chosen with { Source = [.. srcSet], Evidence = evidenceMerged };
                }
                if (dupCollapsed > 0) {
                    filtered = [.. finalMap.Values.OrderByDescending(h => h.Confidence).ThenBy(h => h.Type.ToString())];
                    if (verbose) {
                        try { Console.Error.WriteLine($"[verbose] post-filter dedup collapsed {dupCollapsed} duplicate entries"); } catch { }
                    }
                }
            }

            if (filtered.Count == 0) {
                return 1;
            }

            if (verbose) {
                try {
                    var typeCounts = filtered.GroupBy(h => h.Type).Select(g => $"{g.Key}={g.Count()}");
                    Console.Error.WriteLine($"[verbose] pre-emit counts: {string.Join(",", typeCounts)} showPackageSources={showPackageSources}");
                    if (filtered.Count > 0) {
                        var sample = string.Join(" | ", filtered.Take(5).Select(h => h.Type + ":" + Path.GetFileName(h.Path)));
                        Console.Error.WriteLine($"[verbose] sample: {sample}");
                    }
                    Console.Error.WriteLine("[verbose] marker-before-emit");
                }
                catch { }
            }
            // Evidence filtering & deterministic ordering
            if (evidence) {
                for (var i = 0; i < filtered.Count; i++) {
                    var ev = filtered[i].Evidence;
                    if (ev == null) {
                        continue;
                    }
                    // Filter if key list provided
                    if (evidenceKeyFilter != null) {
                        var toRemove = ev.Keys.Where(k => !evidenceKeyFilter.Contains(k)).ToList();
                        foreach (var k in toRemove) {
                            _ = ev.Remove(k);
                        }
                    }
                    if (ev.Count == 0) {
                        filtered[i] = filtered[i] with { Evidence = null };
                        continue;
                    }
                    // Deterministic ordering: rebuild dictionary with keys sorted ascending
                    var ordered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var k in ev.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)) {
                        ordered[k] = ev[k];
                    }

                    filtered[i] = filtered[i] with { Evidence = ordered };
                }
            }
            else {
                // Ensure evidence suppressed when not requested
                for (var i = 0; i < filtered.Count; i++) {
                    if (filtered[i].Evidence != null) {
                        filtered[i] = filtered[i] with { Evidence = null };
                    }
                }
            }

            // (index persistence removed)

            EmitResults(filtered, json, csv, text, noColor, showPackageSources, scoreBreakdown);
            return 0;
        }
        // BuildCompositeKey removed with cache layer.

        private static string Normalize(string query) {
            // Lightweight alias normalization so single-token shorthand maps to canonical token that sources can match.
            // (Ranking layer has richer bidirectional alias equivalence, but sources doing token presence checks need canonicalization.)
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "vscode", "code" },
                { "ohmyposh", "oh my posh" },
                { "oh-my-posh", "oh my posh" },
                { "oh_my_posh", "oh my posh" },
                { "notepadpp", "notepad++" },
                { "pwsh", "powershell" }
            };
            var trimmed = query.Trim();
            if (map.TryGetValue(trimmed, out var mapped)) {
                trimmed = mapped;
            }

            return string.Join(' ', trimmed.ToLowerInvariant().Split([' '], StringSplitOptions.RemoveEmptyEntries));
        }

        // Centralized scope inference so all sources converge on consistent user/machine classification.
        // This reduces leakage where a machine path appears when --user is specified (and vice-versa) due to
        // per-source heuristics diverging. We keep this conservative: if path clearly under a user profile => User;
        // if under Program Files / ProgramData => Machine; otherwise retain existing.
        private static Scope InferScope(string? path, Scope existing) {
            if (string.IsNullOrWhiteSpace(path)) {
                return existing;
            }
            try {
                var p = path.Replace('/', '\\');
                // Normalize casing once
                var lower = p.ToLowerInvariant();
                // Quick exits for known machine roots
                // Program Files variations
                var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if ((!string.IsNullOrEmpty(pf) && lower.StartsWith(pf, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(pf86) && lower.StartsWith(pf86, StringComparison.OrdinalIgnoreCase))) {
                    return Scope.Machine;
                }
                // ProgramData
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (!string.IsNullOrEmpty(programData) && lower.StartsWith(programData, StringComparison.OrdinalIgnoreCase)) {
                    return Scope.Machine;
                }
                // WindowsApps (MSIX) generally machine-visible but per-user installed; treat as User if under user profile Packages
                // User profile: detect \users\<name>\
                if (lower.Contains("\\users\\", StringComparison.OrdinalIgnoreCase)) {
                    // If under Public treat as machine, else user.
                    return lower.Contains("\\users\\public\\", StringComparison.OrdinalIgnoreCase) ? Scope.Machine : Scope.User;
                }
            }
            catch { /* swallow and keep existing */ }
            return existing;
        }

        // Applies unified scope inference and early pruning for --user / --machine options.
    private static List<AppHit> NormalizeAndFilterScopes(List<AppHit> hits, bool userOnly, bool machineOnly) {
            if (hits.Count == 0) { return hits; }
            var normalized = new List<AppHit>(hits.Count);
            foreach (var h in hits) {
                var inferred = InferScope(h.Path, h.Scope);
                if (inferred != h.Scope) {
                    normalized.Add(h with { Scope = inferred });
                }
                else {
                    normalized.Add(h);
                }
            }
            if (userOnly) {
                normalized = [.. normalized.Where(h => h.Scope == Scope.User)];
            }
            else if (machineOnly) {
                normalized = [.. normalized.Where(h => h.Scope == Scope.Machine)];
            }
            return normalized;
        }

        // Manual PrintHelp removed: System.CommandLine generates help.

        private static ConsoleColor ConfidenceColor(double c) {
            if (c >= 0.80) {
                return ConsoleColor.Green;
            }

            return c >= 0.50 ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
        }

        private static void EmitResults(List<AppHit> filtered, bool json, bool csv, bool text, bool noColor, bool showPackageSources = false, bool scoreBreakdown = false) {
            if (filtered.Count == 0) {
                return;
            }

            if (json) {
                var jsonOut = JsonSerializer.Serialize(filtered, AppLocateJsonContext.Default.IReadOnlyListAppHit);
                Console.Out.WriteLine(jsonOut);
                return;
            }
            if (csv) {
                if (showPackageSources) {
                    Console.Out.WriteLine("Type,Scope,Path,Version,PackageType,Sources,Confidence");
                    foreach (var h in filtered) {
                        var src = string.Join('|', h.Source);
                        Console.Out.WriteLine($"{h.Type},{h.Scope},\"{h.Path}\",{h.Version},{h.PackageType},\"{src}\",{h.Confidence:0.###}");
                    }
                }
                else {
                    Console.Out.WriteLine("Type,Scope,Path,Version,PackageType,Confidence");
                    foreach (var h in filtered) {
                        Console.Out.WriteLine($"{h.Type},{h.Scope},\"{h.Path}\",{h.Version},{h.PackageType},{h.Confidence:0.###}");
                    }
                }
                return;
            }
            if (text) {
                foreach (var h in filtered) {
                    if (noColor) {
                        if (showPackageSources) {
                            Console.Out.WriteLine($"[{h.Confidence:0.00}] {h.Type} {h.Path} (pkg={h.PackageType}; src={string.Join('+', h.Source)})");
                        }
                        else {
                            Console.Out.WriteLine($"[{h.Confidence:0.00}] {h.Type} {h.Path}");
                        }
                        if (scoreBreakdown && h.Breakdown != null) {
                            Console.Out.WriteLine($"    breakdown: token={h.Breakdown.TokenCoverage:0.###} alias={h.Breakdown.AliasEquivalence:0.###} evidence={h.Breakdown.EvidenceBoosts:0.###} multiSrc={h.Breakdown.MultiSource:0.###} penalties={h.Breakdown.PathPenalties + h.Breakdown.NoisePenalties + h.Breakdown.EvidencePenalties:0.###} total={h.Breakdown.Total:0.###}");
                        }
                        continue;
                    }
                    var prevColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConfidenceColor(h.Confidence);
                    Console.Out.Write($"[{h.Confidence:0.00}]");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Out.Write($" {h.Type}");
                    Console.ForegroundColor = prevColor;
                    if (showPackageSources) {
                        Console.Out.WriteLine($" {h.Path} (pkg={h.PackageType}; src={string.Join('+', h.Source)})");
                    }
                    else {
                        Console.Out.WriteLine($" {h.Path}");
                    }
                    if (scoreBreakdown && h.Breakdown != null) {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Out.WriteLine($"    breakdown: token={h.Breakdown.TokenCoverage:0.###} alias={h.Breakdown.AliasEquivalence:0.###} evidence={h.Breakdown.EvidenceBoosts:0.###} multiSrc={h.Breakdown.MultiSource:0.###} penalties={h.Breakdown.PathPenalties + h.Breakdown.NoisePenalties + h.Breakdown.EvidencePenalties:0.###} total={h.Breakdown.Total:0.###}");
                        Console.ForegroundColor = prevColor;
                    }
                }
            }
        }

        private static bool SafePathExists(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return false;
            }

            try {
                return File.Exists(path) || Directory.Exists(path);
            }
            catch { return false; }
        }
    }
}
