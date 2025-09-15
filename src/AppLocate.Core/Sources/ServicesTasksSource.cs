using Microsoft.Win32;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources {
    /// <summary>Enumerates Windows Services (ImagePath) and Scheduled Tasks for executable paths.</summary>
    public sealed class ServicesTasksSource : ISource {
        /// <summary>Unique source identifier used in evidence arrays.</summary>
        public string Name => nameof(ServicesTasksSource);

        /// <summary>
        /// Enumerates Windows Services (ImagePath) and Scheduled Task Command entries, extracting executable paths that
        /// match the query (strict token all-match or fuzzy substring). Emits exe hits plus associated install directory
        /// hits with evidence indicating originating service or task.
        /// </summary>
        /// <param name="query">Raw user query.</param>
        /// <param name="options">Execution options controlling strictness, evidence inclusion, scope filters.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Async stream of exe and install directory hits derived from services / tasks.</returns>
        public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {
            await Task.Yield();
            if (string.IsNullOrWhiteSpace(query)) {
                yield break;
            }

            var norm = query.ToLowerInvariant();
            var tokens = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var seenExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenInstall = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!options.UserOnly) {
                foreach (var hit in EnumerateServices(norm, tokens, options, seenExe, seenInstall, ct)) {
                    if (ct.IsCancellationRequested) {
                        yield break;
                    }

                    yield return hit;
                }
            }
            foreach (var hit in EnumerateTasks(norm, tokens, options, seenExe, seenInstall, ct)) {
                if (ct.IsCancellationRequested) {
                    yield break;
                }

                yield return hit;
            }
        }

        private IEnumerable<AppHit> EnumerateServices(string norm, string[] tokens, SourceOptions options, HashSet<string> seenExe, HashSet<string> seenInstall, CancellationToken ct) {
            RegistryKey? rk = null;
            try { rk = Registry.LocalMachine.OpenSubKey("System\\CurrentControlSet\\Services"); } catch { }
            if (rk == null) {
                yield break;
            }

            foreach (var serviceName in rk.GetSubKeyNames()) {
                if (ct.IsCancellationRequested) {
                    yield break;
                }

                RegistryKey? sk = null; try { sk = rk.OpenSubKey(serviceName); } catch { }
                if (sk == null) {
                    continue;
                }

                string? imagePath = null; string? displayName = null;
                try { imagePath = sk.GetValue("ImagePath") as string; } catch { }
                try { displayName = sk.GetValue("DisplayName") as string; } catch { }
                if (string.IsNullOrWhiteSpace(imagePath)) {
                    continue;
                }

                imagePath = PathUtils.NormalizePath(Environment.ExpandEnvironmentVariables(imagePath));
                var exeCandidate = imagePath != null ? PathUtils.NormalizePath(ExtractExecutablePath(imagePath)) : null;
                if (string.IsNullOrWhiteSpace(exeCandidate) || !File.Exists(exeCandidate)) {
                    continue;
                }

                var fileName = Path.GetFileNameWithoutExtension(exeCandidate) ?? string.Empty;
                var lowerFile = fileName.ToLowerInvariant();
                var lowerService = serviceName.ToLowerInvariant();
                var lowerDisplay = displayName?.ToLowerInvariant();
                var match = options.Strict
                    ? tokens.All(t => lowerFile.Contains(t, StringComparison.Ordinal) || lowerService.Contains(t, StringComparison.Ordinal) || (lowerDisplay?.Contains(t, StringComparison.Ordinal) ?? false))
                    : lowerFile.Contains(norm, StringComparison.Ordinal) || lowerService.Contains(norm, StringComparison.Ordinal) || (lowerDisplay?.Contains(norm, StringComparison.Ordinal) ?? false) || exeCandidate.ToLowerInvariant().Contains(norm, StringComparison.Ordinal);
                if (!match) {
                    continue;
                }

                var scope = ServicesScopeFromPath(exeCandidate);
                if (options.MachineOnly && scope == Scope.User) {
                    continue;
                }

                if (options.UserOnly && scope == Scope.Machine) {
                    continue;
                }

                if (!seenExe.Add(exeCandidate)) {
                    continue;
                }

                Dictionary<string, string>? evidence = null;
                if (options.IncludeEvidence) {
                    evidence = new Dictionary<string, string> { { EvidenceKeys.Service, serviceName }, { EvidenceKeys.ExeName, Path.GetFileName(exeCandidate) } };
                    if (!string.IsNullOrWhiteSpace(displayName)) {
                        evidence[EvidenceKeys.ServiceDisplayName] = displayName!;
                    }

                    var dirName = Path.GetFileName(Path.GetDirectoryName(exeCandidate) ?? string.Empty)?.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(dirName) && (options.Strict ? tokens.All(t => dirName.Contains(t, StringComparison.Ordinal)) : dirName.Contains(norm, StringComparison.Ordinal))) {
                        evidence[EvidenceKeys.DirMatch] = dirName;
                    }
                }
                yield return new AppHit(HitType.Exe, scope, exeCandidate, null, PackageType.EXE, [Name], 0, evidence);
                var dir = Path.GetDirectoryName(exeCandidate);
                if (!string.IsNullOrEmpty(dir) && seenInstall.Add(dir)) {
                    var dirEvidence = evidence;
                    if (options.IncludeEvidence && dirEvidence != null && !dirEvidence.ContainsKey(EvidenceKeys.FromService)) {
                        dirEvidence = new Dictionary<string, string>(dirEvidence) { { EvidenceKeys.FromService, "true" } };
                    }

                    yield return new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.EXE, [Name], 0, dirEvidence);
                }
            }
        }

        private IEnumerable<AppHit> EnumerateTasks(string norm, string[] tokens, SourceOptions options, HashSet<string> seenExe, HashSet<string> seenInstall, CancellationToken ct) {
            var tasksRoot = Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\Tasks");
            if (string.IsNullOrWhiteSpace(tasksRoot) || !Directory.Exists(tasksRoot)) {
                yield break;
            }

            IEnumerable<string> files = [];
            try { files = Directory.EnumerateFiles(tasksRoot, "*", SearchOption.AllDirectories); } catch { }
            foreach (var tf in files) {
                if (ct.IsCancellationRequested) {
                    yield break;
                }

                string? content = null;
                try { content = File.ReadAllText(tf); } catch { }
                if (string.IsNullOrEmpty(content)) {
                    continue;
                }

                if (!content.Contains(".exe", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                if (options.Strict) {
                    if (!tokens.All(t => content.Contains(t, StringComparison.OrdinalIgnoreCase))) {
                        continue;
                    }
                }
                else if (!content.Contains(norm, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                foreach (var exe in ExtractCommands(content)) {
                    if (ct.IsCancellationRequested) {
                        yield break;
                    }

                    var normExe = PathUtils.NormalizePath(exe);
                    if (string.IsNullOrWhiteSpace(normExe) || !File.Exists(normExe)) {
                        continue;
                    }

                    var fileName = Path.GetFileNameWithoutExtension(normExe)?.ToLowerInvariant() ?? string.Empty;
                    var match = options.Strict ? tokens.All(t => fileName.Contains(t, StringComparison.Ordinal) || normExe!.ToLowerInvariant().Contains(t, StringComparison.Ordinal)) : (fileName.Contains(norm, StringComparison.Ordinal) || normExe!.ToLowerInvariant().Contains(norm, StringComparison.Ordinal));
                    if (!match) {
                        continue;
                    }

                    if (!seenExe.Add(normExe!)) {
                        continue;
                    }

                    var scope = ServicesScopeFromPath(normExe!);
                    if (options.MachineOnly && scope == Scope.User) {
                        continue;
                    }

                    if (options.UserOnly && scope == Scope.Machine) {
                        continue;
                    }

                    Dictionary<string, string>? evidence = null;
                    if (options.IncludeEvidence) {
                        var taskName = tf.StartsWith(tasksRoot, StringComparison.OrdinalIgnoreCase) ? tf[tasksRoot.Length..].TrimStart(Path.DirectorySeparatorChar) : tf;
                        evidence = new Dictionary<string, string> { { EvidenceKeys.TaskFile, tf }, { EvidenceKeys.TaskName, taskName }, { EvidenceKeys.ExeName, Path.GetFileName(normExe) } };
                        var dirName = Path.GetFileName(Path.GetDirectoryName(normExe!) ?? string.Empty)?.ToLowerInvariant();
                        if (!string.IsNullOrEmpty(dirName) && (options.Strict ? tokens.All(t => dirName.Contains(t, StringComparison.Ordinal)) : dirName.Contains(norm, StringComparison.Ordinal))) {
                            evidence[EvidenceKeys.DirMatch] = dirName;
                        }
                    }
                    yield return new AppHit(HitType.Exe, scope, normExe!, null, PackageType.EXE, [Name], 0, evidence);
                    var dir = Path.GetDirectoryName(normExe!);
                    if (!string.IsNullOrEmpty(dir) && seenInstall.Add(dir)) {
                        var dirEvidence = evidence;
                        if (options.IncludeEvidence && dirEvidence != null && !dirEvidence.ContainsKey(EvidenceKeys.FromTask)) {
                            dirEvidence = new Dictionary<string, string>(dirEvidence) { { EvidenceKeys.FromTask, "true" } };
                        }

                        yield return new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.EXE, [Name], 0, dirEvidence);
                    }
                }
            }
        }

        private static string ExtractExecutablePath(string imagePath) {
            try {
                var idxExe = imagePath.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (idxExe == -1) {
                    return string.Empty;
                }

                var startQuote = imagePath.LastIndexOf('"', idxExe);
                var start = startQuote >= 0 ? startQuote + 1 : 0;
                var candidate = imagePath[start..(idxExe + 4)];
                return candidate.Trim('"');
            }
            catch { return string.Empty; }
        }

        private static IEnumerable<string> ExtractCommands(string xmlContent) {
            var pos = 0;
            while (true) {
                if (pos >= xmlContent.Length) {
                    yield break;
                }

                var start = xmlContent.IndexOf("<Command>", pos, StringComparison.OrdinalIgnoreCase);
                if (start == -1) {
                    yield break;
                }

                var end = xmlContent.IndexOf("</Command>", start, StringComparison.OrdinalIgnoreCase);
                if (end == -1) {
                    yield break;
                }

                var innerStart = start + "<Command>".Length;
                var inner = xmlContent[innerStart..end].Trim();
                pos = end + "</Command>".Length;
                if (string.IsNullOrEmpty(inner)) {
                    continue;
                }

                var exe = ExtractExecutablePath(inner);
                if (!string.IsNullOrEmpty(exe)) {
                    yield return exe;
                }
            }
        }

        private static Scope ServicesScopeFromPath(string path) {
            try {
                var lower = path.ToLowerInvariant();
                return lower.Contains("\\users\\", StringComparison.Ordinal) ? Scope.User : Scope.Machine;
            }
            catch { return Scope.Machine; }
        }
    }
}
