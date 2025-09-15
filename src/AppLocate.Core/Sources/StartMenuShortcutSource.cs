using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources {
    /// <summary>Enumerates Start Menu .lnk shortcuts to infer executables and install directories.</summary>
    public sealed class StartMenuShortcutSource : ISource {
        // Reusable delimiter array to avoid repeated allocations (CA1861)
        private static readonly char[] StrictSplitDelimiters = ['.', '-', '_', ' '];
        private static readonly string[] SingleNameSourceArray = [nameof(StartMenuShortcutSource)];
        /// <summary>Unique source identifier used in evidence arrays.</summary>
        public string Name => nameof(StartMenuShortcutSource);

        private static string[] GetUserRoots() =>
        [
            Environment.ExpandEnvironmentVariables("%AppData%\\Microsoft\\Windows\\Start Menu\\Programs")
        ];

        private static string[] GetCommonRoots() =>
        [
            Environment.ExpandEnvironmentVariables("%ProgramData%\\Microsoft\\Windows\\Start Menu\\Programs").Replace("\n", string.Empty)
        ];

        /// <summary>
        /// Recursively enumerates Start Menu shortcut (.lnk) files (user + common) and resolves targets to executables
        /// matching the query (strict token all-match or fuzzy substring/collapsed). Emits exe + install directory hits
        /// with Shortcut evidence key when requested.
        /// </summary>
        /// <param name="query">Raw user query.</param>
        /// <param name="options">Execution options controlling scope, strictness, evidence inclusion.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Async stream of exe and install directory hits derived from shortcuts.</returns>
        public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {
            await Task.Yield();
            if (string.IsNullOrWhiteSpace(query)) {
                yield break;
            }
            var norm = query.ToLowerInvariant();
            var tokens = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!options.MachineOnly) {
                foreach (var r in Enumerate(GetUserRoots(), Scope.User, norm, tokens, options, dedup, ct)) {
                    yield return r;
                }
            }
            if (!options.UserOnly) {
                foreach (var r in Enumerate(GetCommonRoots(), Scope.Machine, norm, tokens, options, dedup, ct)) {
                    yield return r;
                }
            }
        }

        private static IEnumerable<AppHit> Enumerate(IEnumerable<string> roots, Scope scope, string norm, string[] tokens, SourceOptions options, HashSet<string> dedup, CancellationToken ct) {
            foreach (var root in roots) {
                if (ct.IsCancellationRequested) {
                    yield break;
                }
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) {
                    continue;
                }
                IEnumerable<string> files = [];
                try {
                    files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
                }
                catch { }
                foreach (var lnk in files) {
                    if (ct.IsCancellationRequested) {
                        yield break;
                    }
                    var fileName = Path.GetFileNameWithoutExtension(lnk).ToLowerInvariant();
                    if (!Matches(fileName, norm, tokens, options.Strict)) {
                        continue;
                    }
                    string? target = null;
                    try { target = ResolveShortcut(lnk); } catch { }
                    if (string.IsNullOrWhiteSpace(target)) {
                        continue;
                    }
                    target = PathUtils.NormalizePath(Environment.ExpandEnvironmentVariables(target));
                    if (string.IsNullOrWhiteSpace(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                    if (!File.Exists(target)) {
                        continue;
                    }
                    if (!dedup.Add(target)) {
                        continue;
                    }
                    var evidence = options.IncludeEvidence ? new Dictionary<string, string> { { EvidenceKeys.Shortcut, lnk } } : null;
                    yield return new AppHit(HitType.Exe, scope, target, null, PackageType.EXE, SingleNameSourceArray, 0, evidence);
                    var dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir) && dedup.Add(dir + "::install")) {
                        yield return new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.EXE, SingleNameSourceArray, 0, evidence);
                    }
                }
            }
        }

        private static bool Matches(string fileNameLower, string norm, string[] tokens, bool strict) {
            var parts = fileNameLower.Split(StrictSplitDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!strict) {
                if (fileNameLower.Contains(norm)) { return true; }
                if (tokens.Length > 1) {
                    var collapsed = string.Concat(tokens);
                    if (fileNameLower.Contains(collapsed)) { return true; }
                    var all = true;
                    foreach (var t in tokens) { if (!fileNameLower.Contains(t)) { all = false; break; } }
                    if (all) { return true; }
                }
                return false;
            }
            foreach (var t in tokens) {
                var found = false;
                foreach (var p in parts) { if (p.Contains(t)) { found = true; break; } }
                if (!found) { return false; }
            }
            return true;
        }

        private static string? ResolveShortcut(string lnk) {
            try {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) { return null; }
                dynamic shell = Activator.CreateInstance(shellType)!;
                try {
                    dynamic sc = shell.CreateShortcut(lnk);
                    var target = sc.TargetPath as string;
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sc);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                    return target;
                }
                catch {
                    try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); } catch { }
                }
            }
            catch { }
            return null;
        }
    }
}
