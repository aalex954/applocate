using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources {
    /// <summary>Enumerates running processes for matching executables.</summary>
    public sealed class ProcessSource : ISource {
        /// <summary>Unique source identifier used in evidence arrays.</summary>
        public string Name => nameof(ProcessSource);

        /// <summary>
        /// Scans currently running system processes (best-effort; may skip access denied processes) and yields executable
        /// hits whose process name or main module path matches the query tokens (strict all-match or fuzzy substring).
        /// Evidence can include process id, process name, and exe file name.
        /// </summary>
        /// <param name="query">Raw user query.</param>
        /// <param name="options">Execution options controlling strictness, evidence inclusion, scope filters.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Async stream of running process exe and inferred install directory hits.</returns>
        public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {
            await Task.Yield();
            if (string.IsNullOrWhiteSpace(query)) {
                yield break;
            }

            var norm = query.ToLowerInvariant();
            var processTokens = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            System.Diagnostics.Process[] procs;
            try { procs = System.Diagnostics.Process.GetProcesses(); }
            catch { yield break; }

            var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in procs) {
                if (ct.IsCancellationRequested) {
                    yield break;
                }

                string? name = null;
                string? mainModulePath = null;
                try { name = p.ProcessName; } catch { }
                try { mainModulePath = p.MainModule?.FileName; } catch { }
                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(mainModulePath)) {
                    continue;
                }

                var nameLower = name?.ToLowerInvariant();
                var match = options.Strict
                    ? nameLower != null && processTokens.All(t => nameLower.Contains(t, StringComparison.Ordinal))
                    : (nameLower != null && nameLower.Contains(norm, StringComparison.Ordinal)) || (mainModulePath != null && mainModulePath.ToLowerInvariant().Contains(norm, StringComparison.Ordinal));
                if (!match) {
                    continue;
                }

                if (!string.IsNullOrEmpty(mainModulePath) && File.Exists(mainModulePath)) {
                    if (!dedup.Add(mainModulePath)) {
                        continue;
                    }

                    var scope = InferScope(mainModulePath);
                    Dictionary<string, string>? evidence = null;
                    if (options.IncludeEvidence) {
                        evidence = new Dictionary<string, string> { { "ProcessId", p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
                        if (!string.IsNullOrEmpty(name)) {
                            evidence["ProcessName"] = name;
                        }

                        var exeName = Path.GetFileName(mainModulePath);
                        if (!string.IsNullOrEmpty(exeName)) {
                            evidence["ExeName"] = exeName;
                        }
                    }
                    yield return new AppHit(HitType.Exe, scope, mainModulePath, null, PackageType.EXE, [Name], 0, evidence);
                    var dir = Path.GetDirectoryName(mainModulePath);
                    if (!string.IsNullOrEmpty(dir) && dedup.Add(dir + "::install")) {
                        var dirEvidence = evidence;
                        if (options.IncludeEvidence && dirEvidence != null && !dirEvidence.ContainsKey("DirMatch")) {
                            dirEvidence = new Dictionary<string, string>(dirEvidence) { { "DirMatch", "true" } };
                        }

                        yield return new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.EXE, [Name], 0, dirEvidence);
                    }
                }
            }
        }

        private static Scope InferScope(string path) {
            try {
                var lower = path.ToLowerInvariant();
                return lower.Contains("\\users\\", StringComparison.Ordinal) ? Scope.User : Scope.Machine;
            }
            catch { return Scope.Machine; }
        }
    }
}
