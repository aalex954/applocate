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

        private static readonly string[] ScoopRoots = GetScoopRoots();

        private static string[] GetScoopRoots() {
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

            return [.. roots];
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<AppHit> QueryAsync(
            string query,
            SourceOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {
            await Task.Yield();

            if (string.IsNullOrWhiteSpace(query) || ScoopRoots.Length == 0) {
                yield break;
            }

            var normalizedQuery = query.ToLowerInvariant();
            var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var root in ScoopRoots) {
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
                if (!Directory.Exists(appsDir)) {
                    continue;
                }

                string[] appDirs;
                try {
                    appDirs = Directory.GetDirectories(appsDir);
                }
                catch {
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
                    if (!Directory.Exists(currentDir)) {
                        // Try to find latest version directory
                        var versionDirs = SafeGetDirectories(appDir)
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
                    if (Directory.Exists(persistDir)) {
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

        private static (string? Version, List<string> ExeHints) ReadManifest(string manifestPath) {
            if (!File.Exists(manifestPath)) {
                return (null, []);
            }

            try {
                var json = File.ReadAllText(manifestPath);
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
                case JsonValueKind.Undefined:
                    break;
                case JsonValueKind.Object:
                    break;
                case JsonValueKind.Number:
                    break;
                case JsonValueKind.True:
                    break;
                case JsonValueKind.False:
                    break;
                case JsonValueKind.Null:
                    break;
                default:
                    break;
            }
        }

        private static IEnumerable<string> FindExecutables(string dir, List<string> exeHints) {
            // First try manifest hints
            foreach (var hint in exeHints) {
                var fullPath = Path.Combine(dir, hint.Replace('/', '\\'));
                if (File.Exists(fullPath)) {
                    yield return fullPath;
                }
            }

            // If no hints or none found, scan top-level for .exe files
            if (exeHints.Count == 0) {
                foreach (var exe in SafeGetFiles(dir, "*.exe")) {
                    yield return exe;
                }
            }
        }

        private static string[] SafeGetDirectories(string path) {
            try {
                return Directory.GetDirectories(path);
            }
            catch {
                return [];
            }
        }

        private static string[] SafeGetFiles(string path, string pattern) {
            try {
                return Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly);
            }
            catch {
                return [];
            }
        }
    }
}
