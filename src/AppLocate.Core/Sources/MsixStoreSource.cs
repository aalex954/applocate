using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources {
    /// <summary>Enumerates MSIX / Store packages (PowerShell or env injected) to produce install dir and exe hits.</summary>
    public sealed class MsixStoreSource : ISource {
        /// <summary>Unique source identifier used in evidence arrays.</summary>
        public string Name => nameof(MsixStoreSource);

        internal interface IMsixPackageProvider {
            IEnumerable<(string name, string family, string install, string version)> Enumerate();
        }
        private sealed class PowerShellMsixProvider : IMsixPackageProvider {
            public IEnumerable<(string name, string family, string install, string version)> Enumerate() {
                var packages = new List<(string, string, string, string)>();
                try {
                    using var p = new System.Diagnostics.Process();
                    p.StartInfo.FileName = Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe");
                    p.StartInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"$ErrorActionPreference='SilentlyContinue'; Get-AppxPackage | ForEach-Object { \"$($_.Name)|$($_.PackageFamilyName)|$($_.InstallLocation)|$($_.Version)\" }\"";
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;
                    p.StartInfo.CreateNoWindow = true;
                    _ = p.Start();
                    while (!p.StandardOutput.EndOfStream) {
                        var line = p.StandardOutput.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) {
                            continue;
                        }

                        var parts = line.Split('|');
                        if (parts.Length < 4) {
                            continue;
                        }

                        packages.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), parts[3].Trim()));
                    }
                    try { if (!p.HasExited) { p.Kill(true); } } catch { }
                }
                catch { }
                return packages;
            }
        }
        private sealed class EnvMsixProvider : IMsixPackageProvider {
            public IEnumerable<(string name, string family, string install, string version)> Enumerate() {
                var json = Environment.GetEnvironmentVariable("APPLOCATE_MSIX_FAKE");
                if (string.IsNullOrWhiteSpace(json)) {
                    return [];
                }

                try {
                    var arr = System.Text.Json.JsonDocument.Parse(json).RootElement;
                    if (arr.ValueKind != System.Text.Json.JsonValueKind.Array) {
                        return [];
                    }

                    var list = new List<(string, string, string, string)>();
                    foreach (var el in arr.EnumerateArray()) {
                        var name = el.GetProperty("name").GetString() ?? string.Empty;
                        var family = el.GetProperty("family").GetString() ?? name + ".fake";
                        var install = el.GetProperty("install").GetString() ?? string.Empty;
                        var version = el.TryGetProperty("version", out var v) ? (v.GetString() ?? "") : "1.0.0.0";
                        if (name.Length == 0 || install.Length == 0) {
                            continue;
                        }

                        list.Add((name, family, install, version));
                    }
                    return list;
                }
                catch { return []; }
            }
        }
        private static IMsixPackageProvider CreateProvider() {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLOCATE_MSIX_FAKE"))
                ? new EnvMsixProvider()
                : new PowerShellMsixProvider();
        }

        /// <summary>
        /// Enumerates MSIX / Store packages (via PowerShell or injected JSON provider) and yields install directory and
        /// executable hits whose package name or family name match the query (strict token all-match or fuzzy substring).
        /// Evidence may include package family, name, version and exe name.
        /// </summary>
        /// <param name="query">Raw user query.</param>
        /// <param name="options">Execution options controlling scope, strictness, evidence inclusion.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Async stream of package install and exe hits.</returns>
        public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {
            await Task.Yield();
            if (string.IsNullOrWhiteSpace(query)) {
                yield break;
            }

            var norm = query.ToLowerInvariant();
            var tokens = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var provider = CreateProvider();
            var packages = provider.Enumerate().ToList();
            if (packages.Count == 0) {
                yield break;
            }

            var seenInstall = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, family, install, version) in packages) {
                if (ct.IsCancellationRequested) {
                    yield break;
                }

                bool match;
                var nameLower = name.ToLowerInvariant();
                var famLower = family.ToLowerInvariant();
                if (options.Strict) {
                    match = tokens.All(t => nameLower.Contains(t) || famLower.Contains(t));
                }
                else {
                    match = nameLower.Contains(norm) || famLower.Contains(norm);
                    if (!match && tokens.Length > 1) {
                        // Multi-token fuzzy: require all tokens to be present across name or family
                        match = tokens.All(t => nameLower.Contains(t) || famLower.Contains(t));
                    }
                }
                if (!match) {
                    continue;
                }

                var scope = Scope.User;
                if (options.MachineOnly) {
                    continue;
                }

                // Accept Program Files WindowsApps paths even if Directory.Exists returns false (ACL prevents listing)
                static bool LooksLikeWindowsApps(string p) {
                    if (string.IsNullOrWhiteSpace(p)) { return false; }
                    try {
                        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                        if (!string.IsNullOrEmpty(pf)) {
                            var wa = Path.Combine(pf, "WindowsApps");
                            if (p.StartsWith(wa, StringComparison.OrdinalIgnoreCase)) {
                                return true;
                            }
                        }
                    }
                    catch { }
                    return p.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
                }

                var installLooksValid = !string.IsNullOrWhiteSpace(install) && (Directory.Exists(install) || LooksLikeWindowsApps(install));
                if (installLooksValid && seenInstall.Add(install)) {
                    Dictionary<string, string>? evidence = null;
                    if (options.IncludeEvidence) {
                        evidence = new Dictionary<string, string> { { EvidenceKeys.PackageFamilyName, family }, { EvidenceKeys.PackageName, name } };
                        if (!string.IsNullOrEmpty(version)) {
                            evidence[EvidenceKeys.PackageVersion] = version;
                        }
                    }
                    yield return new AppHit(HitType.InstallDir, scope, install, version, PackageType.MSIX, [Name], 0, evidence);

                    // First attempt AppxManifest.xml parse for declared Application Executable entries; fall back to raw directory scan.
                    var manifestPath = Path.Combine(install, "AppxManifest.xml");
                    var manifestExes = new List<string>();
                    try {
                        if (File.Exists(manifestPath)) {
                            var xml = File.ReadAllText(manifestPath);
                            // Very lightweight parse: look for Executable="..." attributes in <Application ...>
                            // Avoid full XML DOM to reduce allocations; handle quotes conservatively.
                            var idx = 0;
                            while (idx < xml.Length) {
                                var appTag = xml.IndexOf("<Application", idx, StringComparison.OrdinalIgnoreCase);
                                if (appTag < 0) { break; }
                                var close = xml.IndexOf('>', appTag + 12);
                                if (close < 0) { break; }
                                var segment = xml.AsSpan(appTag, close - appTag).ToString();
                                var exeAttrIdx = segment.IndexOf("Executable=", StringComparison.OrdinalIgnoreCase);
                                if (exeAttrIdx >= 0) {
                                    var quoteStart = segment.IndexOf('"', exeAttrIdx);
                                    if (quoteStart >= 0) {
                                        var quoteEnd = segment.IndexOf('"', quoteStart + 1);
                                        if (quoteEnd > quoteStart) {
                                            var rel = segment.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).Trim();
                                            if (rel.Length > 0) {
                                                // Executable may be relative; combine.
                                                var abs = Path.Combine(install, rel.Replace('/', Path.DirectorySeparatorChar));
                                                if (File.Exists(abs)) {
                                                    manifestExes.Add(abs);
                                                }
                                            }
                                        }
                                    }
                                }
                                idx = close + 1;
                            }
                        }
                    }
                    catch { }

                    IEnumerable<string> exes = manifestExes.Count > 0 ? manifestExes : [];
                    if (manifestExes.Count == 0) {
                        try { exes = Directory.EnumerateFiles(install, "*.exe", SearchOption.TopDirectoryOnly); } catch { }
                    }
                    foreach (var exe in exes) {
                        if (ct.IsCancellationRequested) {
                            yield break;
                        }

                        if (!File.Exists(exe)) {
                            continue;
                        }

                        var exeNameLower = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                        var exeMatch = options.Strict
                            ? tokens.All(exeNameLower.Contains)
                            : exeNameLower.Contains(norm) || match || (tokens.Length > 1 && tokens.All(exeNameLower.Contains));
                        if (!exeMatch) {
                            continue;
                        }

                        if (!seenExe.Add(exe)) {
                            continue;
                        }

                        var exeEvidence = evidence;
                        if (options.IncludeEvidence) {
                            exeEvidence = new Dictionary<string, string>(evidence ?? []) { { EvidenceKeys.ExeName, Path.GetFileName(exe) } };
                            if (manifestExes.Count > 0) {
                                exeEvidence["MsixManifest"] = "1";
                            }
                        }

                        yield return new AppHit(HitType.Exe, scope, exe, version, PackageType.MSIX, [Name], 0, exeEvidence);
                    }
                }
            }
        }
    }
}
