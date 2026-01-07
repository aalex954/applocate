using System.Text.Json;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources {
    /// <summary>
    /// Discovers applications installed via the Scoop package manager.
    /// Scoop installs apps to ~/scoop/apps (user) or C:\ProgramData\scoop\apps (global).
    /// Each app has a "current" symlink to the active version with a manifest.json.
    /// </summary>
    public sealed class ScoopSource : ISource {
        /// <inheritdoc />
        public string Name => nameof(ScoopSource);

        /// <summary>Provider abstraction for testability.</summary>
        internal interface IScoopProvider {
            IEnumerable<string> GetRoots();
            bool DirectoryExists(string path);
            string[] GetDirectories(string path);
            string[] GetFiles(string path, string pattern);
            string? ReadFileText(string path);
            bool FileExists(string path);
        }

        /// <summary>Default provider that uses real filesystem.</summary>
        private sealed class FileSystemScoopProvider : IScoopProvider {
            public IEnumerable<string> GetRoots() {
                var roots = new List<string>();

                // User scoop: prefer SCOOP env var, fallback to ~/scoop
                var scoopEnv = Environment.GetEnvironmentVariable("SCOOP");
                if (!string.IsNullOrEmpty(scoopEnv) && Directory.Exists(scoopEnv)) {
                    roots.Add(scoopEnv);
                }
                else {
                    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var userScoop = Path.Combine(userProfile, "scoop");
                    if (Directory.Exists(userScoop)) {
                        roots.Add(userScoop);
                    }
                }

                // Global scoop: prefer SCOOP_GLOBAL env var, fallback to C:\ProgramData\scoop
                var globalEnv = Environment.GetEnvironmentVariable("SCOOP_GLOBAL");
                if (!string.IsNullOrEmpty(globalEnv) && Directory.Exists(globalEnv)) {
                    roots.Add(globalEnv);
                }
                else {
                    const string globalScoop = @"C:\ProgramData\scoop";
                    if (Directory.Exists(globalScoop)) {
                        roots.Add(globalScoop);
                    }
                }

                return roots;
            }

            public bool DirectoryExists(string path) => Directory.Exists(path);
            public string[] GetDirectories(string path) {
                try { return Directory.GetDirectories(path); }
                catch { return []; }
            }
            public string[] GetFiles(string path, string pattern) {
                try { return Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly); }
                catch { return []; }
            }
            public string? ReadFileText(string path) {
                try { return File.Exists(path) ? File.ReadAllText(path) : null; }
                catch { return null; }
            }
            public bool FileExists(string path) => File.Exists(path);
        }

        /// <summary>Fake provider for deterministic testing via APPLOCATE_SCOOP_FAKE env var.</summary>
        private sealed class FakeScoopProvider : IScoopProvider {
            private readonly Dictionary<string, FakeApp> _apps = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> _roots = [];

            public FakeScoopProvider(string json) {
                try {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("roots", out var rootsArr)) {
                        foreach (var r in rootsArr.EnumerateArray()) {
                            var rPath = r.GetString();
                            if (!string.IsNullOrEmpty(rPath)) _roots.Add(rPath);
                        }
                    }

                    if (root.TryGetProperty("apps", out var appsArr)) {
                        foreach (var app in appsArr.EnumerateArray()) {
                            var name = app.GetProperty("name").GetString() ?? "";
                            var rootPath = app.TryGetProperty("root", out var rp) ? rp.GetString() ?? _roots.FirstOrDefault() ?? "" : _roots.FirstOrDefault() ?? "";
                            var version = app.TryGetProperty("version", out var v) ? v.GetString() : null;
                            var exes = new List<string>();
                            if (app.TryGetProperty("exes", out var exArr)) {
                                foreach (var e in exArr.EnumerateArray()) {
                                    var ePath = e.GetString();
                                    if (!string.IsNullOrEmpty(ePath)) exes.Add(ePath);
                                }
                            }
                            var hasPersist = app.TryGetProperty("persist", out var p) && p.GetBoolean();
                            var manifestBin = new List<string>();
                            if (app.TryGetProperty("bin", out var binArr)) {
                                foreach (var b in binArr.EnumerateArray()) {
                                    var bPath = b.GetString();
                                    if (!string.IsNullOrEmpty(bPath)) manifestBin.Add(bPath);
                                }
                            }

                            _apps[Path.Combine(rootPath, "apps", name)] = new FakeApp(name, rootPath, version, exes, hasPersist, manifestBin);
                        }
                    }
                }
                catch { }
            }

            public IEnumerable<string> GetRoots() => _roots;
            public bool DirectoryExists(string path) {
                // Check if it's a root, apps dir, app dir, current dir, or persist dir
                if (_roots.Contains(path, StringComparer.OrdinalIgnoreCase)) return true;
                if (_roots.Any(r => path.Equals(Path.Combine(r, "apps"), StringComparison.OrdinalIgnoreCase))) return true;
                if (_apps.ContainsKey(path)) return true;
                if (_apps.Keys.Any(k => path.Equals(Path.Combine(k, "current"), StringComparison.OrdinalIgnoreCase))) return true;
                // Persist directories
                foreach (var app in _apps.Values) {
                    if (app.HasPersist && path.Equals(Path.Combine(app.RootPath, "persist", app.Name), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
            public string[] GetDirectories(string path) {
                // Return app directories under apps/
                var matching = _apps.Keys.Where(k => Path.GetDirectoryName(k)?.Equals(path, StringComparison.OrdinalIgnoreCase) == true).ToArray();
                if (matching.Length > 0) return matching;
                // Return version dirs under app (just "current")
                if (_apps.ContainsKey(path)) return [Path.Combine(path, "current")];
                return [];
            }
            public string[] GetFiles(string path, string pattern) {
                // Return exe files in current directory
                var appDir = _apps.Keys.FirstOrDefault(k => path.Equals(Path.Combine(k, "current"), StringComparison.OrdinalIgnoreCase));
                if (appDir != null && pattern == "*.exe") {
                    return _apps[appDir].Exes.Select(e => Path.Combine(path, e)).ToArray();
                }
                return [];
            }
            public string? ReadFileText(string path) {
                // Return manifest.json content
                if (path.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)) {
                    var currentDir = Path.GetDirectoryName(path);
                    var appDir = _apps.Keys.FirstOrDefault(k => currentDir?.Equals(Path.Combine(k, "current"), StringComparison.OrdinalIgnoreCase) == true);
                    if (appDir != null) {
                        var app = _apps[appDir];
                        var binJson = app.ManifestBin.Count > 0
                            ? $",\"bin\":[{string.Join(",", app.ManifestBin.Select(b => $"\"{b}\""))}]"
                            : "";
                        return $"{{\"version\":\"{app.Version ?? "1.0.0"}\"{binJson}}}";
                    }
                }
                return null;
            }
            public bool FileExists(string path) {
                if (path.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)) {
                    var currentDir = Path.GetDirectoryName(path);
                    return _apps.Keys.Any(k => currentDir?.Equals(Path.Combine(k, "current"), StringComparison.OrdinalIgnoreCase) == true);
                }
                // Check exe files
                foreach (var app in _apps.Values) {
                    var currentPath = Path.Combine(app.RootPath, "apps", app.Name, "current");
                    foreach (var exe in app.Exes) {
                        if (path.Equals(Path.Combine(currentPath, exe), StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    foreach (var bin in app.ManifestBin) {
                        if (path.Equals(Path.Combine(currentPath, bin.Replace('/', '\\')), StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                return false;
            }

            private sealed record FakeApp(string Name, string RootPath, string? Version, List<string> Exes, bool HasPersist, List<string> ManifestBin);
        }

        private static IScoopProvider CreateProvider() {
            var fakeJson = Environment.GetEnvironmentVariable("APPLOCATE_SCOOP_FAKE");
            return !string.IsNullOrEmpty(fakeJson) ? new FakeScoopProvider(fakeJson) : new FileSystemScoopProvider();
        }

        private readonly IScoopProvider _provider;

        /// <summary>Creates a new ScoopSource with the default or fake provider.</summary>
        public ScoopSource() : this(CreateProvider()) { }

        /// <summary>Creates a new ScoopSource with the specified provider (for testing).</summary>
        internal ScoopSource(IScoopProvider provider) => _provider = provider;

        /// <inheritdoc />
        public async IAsyncEnumerable<AppHit> QueryAsync(
            string query,
            SourceOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {
            await Task.Yield();

            var roots = _provider.GetRoots().ToList();
            if (string.IsNullOrWhiteSpace(query) || roots.Count == 0) {
                yield break;
            }

            var normalizedQuery = query.ToLowerInvariant();
            var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var root in roots) {
                if (ct.IsCancellationRequested) {
                    yield break;
                }

                var isGlobal = root.StartsWith(@"C:\ProgramData", StringComparison.OrdinalIgnoreCase);
                var scope = isGlobal ? Scope.Machine : Scope.User;

                // Skip based on scope filter
                if (options.UserOnly && scope == Scope.Machine) {
                    continue;
                }

                if (options.MachineOnly && scope == Scope.User) {
                    continue;
                }

                var appsDir = Path.Combine(root, "apps");
                if (!_provider.DirectoryExists(appsDir)) {
                    continue;
                }

                var appDirs = _provider.GetDirectories(appsDir);
                if (appDirs.Length == 0) {
                    continue;
                }

                foreach (var appDir in appDirs) {
                    if (ct.IsCancellationRequested) {
                        yield break;
                    }

                    var appName = Path.GetFileName(appDir);
                    var appNameLower = appName.ToLowerInvariant();

                    // Match check
                    if (!MatchesQuery(appNameLower, normalizedQuery, queryTokens)) {
                        continue;
                    }

                    // Look for current version (symlink or directory)
                    var currentDir = Path.Combine(appDir, "current");
                    if (!_provider.DirectoryExists(currentDir)) {
                        // Try to find latest version directory
                        var versionDirs = _provider.GetDirectories(appDir)
                            .Where(d => !Path.GetFileName(d).Equals("current", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(Path.GetFileName)
                            .FirstOrDefault();
                        if (versionDirs == null) {
                            continue;
                        }

                        currentDir = versionDirs;
                    }

                    // Read manifest.json for metadata
                    var manifestPath = Path.Combine(currentDir, "manifest.json");
                    var (version, exeHints) = ReadManifest(manifestPath);

                    var evidence = options.IncludeEvidence
                        ? new Dictionary<string, string> {
                            ["ScoopApp"] = appName,
                            ["ScoopRoot"] = root
                        }
                        : null;

                    // Emit install_dir hit
                    yield return new AppHit(
                        Type: HitType.InstallDir,
                        Scope: scope,
                        Path: PathUtils.NormalizePath(currentDir) ?? currentDir,
                        Version: version,
                        PackageType: PackageType.Scoop,
                        Source: [Name],
                        Confidence: 0f, // Set by ranker
                        Evidence: evidence
                    );

                    // Find and emit exe hits
                    foreach (var exe in FindExecutables(currentDir, exeHints)) {
                        if (ct.IsCancellationRequested) {
                            yield break;
                        }

                        var exeEvidence = options.IncludeEvidence
                            ? new Dictionary<string, string>(evidence!) { ["ExeName"] = Path.GetFileName(exe) }
                            : null;

                        yield return new AppHit(
                            Type: HitType.Exe,
                            Scope: scope,
                            Path: PathUtils.NormalizePath(exe) ?? exe,
                            Version: version,
                            PackageType: PackageType.Scoop,
                            Source: [Name],
                            Confidence: 0f,
                            Evidence: exeEvidence
                        );
                    }

                    // Check for persist directory (config/data)
                    var persistDir = Path.Combine(root, "persist", appName);
                    if (_provider.DirectoryExists(persistDir)) {
                        var persistEvidence = options.IncludeEvidence
                            ? new Dictionary<string, string>(evidence!) { ["PersistDir"] = "true" }
                            : null;

                        yield return new AppHit(
                            Type: HitType.Data,
                            Scope: scope,
                            Path: PathUtils.NormalizePath(persistDir) ?? persistDir,
                            Version: version,
                            PackageType: PackageType.Scoop,
                            Source: [Name],
                            Confidence: 0f,
                            Evidence: persistEvidence
                        );
                    }
                }
            }
        }

        private static bool MatchesQuery(string appNameLower, string normalizedQuery, string[] queryTokens) {
            // Direct substring match
            if (appNameLower.Contains(normalizedQuery)) {
                return true;
            }

            // All query tokens present in app name
            if (queryTokens.All(appNameLower.Contains)) {
                return true;
            }

            // Query contains app name
            return normalizedQuery.Contains(appNameLower);
        }

        private (string? Version, List<string> ExeHints) ReadManifest(string manifestPath) {
            var json = _provider.ReadFileText(manifestPath);
            if (string.IsNullOrEmpty(json)) {
                return (null, []);
            }

            try {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? version = null;
                if (root.TryGetProperty("version", out var vProp)) {
                    version = vProp.GetString();
                }

                var exeHints = new List<string>();

                // Check "bin" property - can be string, array, or array of arrays
                if (root.TryGetProperty("bin", out var binProp)) {
                    ExtractBinPaths(binProp, exeHints);
                }

                return (version, exeHints);
            }
            catch {
                return (null, []);
            }
        }

        private static void ExtractBinPaths(JsonElement binProp, List<string> exeHints) {
            switch (binProp.ValueKind) {
                case JsonValueKind.String:
                    var s = binProp.GetString();
                    if (!string.IsNullOrEmpty(s)) {
                        exeHints.Add(s);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in binProp.EnumerateArray()) {
                        if (item.ValueKind == JsonValueKind.String) {
                            var str = item.GetString();
                            if (!string.IsNullOrEmpty(str)) {
                                exeHints.Add(str);
                            }
                        }
                        else if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0) {
                            // First element is the path
                            var first = item[0].GetString();
                            if (!string.IsNullOrEmpty(first)) {
                                exeHints.Add(first);
                            }
                        }
                    }
                    break;
                default:
                    break;
            }
        }

        private IEnumerable<string> FindExecutables(string dir, List<string> exeHints) {
            // First try manifest hints
            foreach (var hint in exeHints) {
                var fullPath = Path.Combine(dir, hint.Replace('/', '\\'));
                if (_provider.FileExists(fullPath)) {
                    yield return fullPath;
                }
            }

            // If no hints or none found, scan top-level for .exe files
            if (exeHints.Count == 0) {
                foreach (var exe in _provider.GetFiles(dir, "*.exe")) {
                    yield return exe;
                }
            }
        }
    }
}
