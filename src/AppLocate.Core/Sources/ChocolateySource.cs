using System.Xml.Linq;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources {
    /// <summary>
    /// Discovers applications installed via the Chocolatey package manager.
    /// Chocolatey installs to C:\ProgramData\chocolatey by default.
    /// Package metadata is in lib\{package}\.chocolatey\{package}.{version}\.nupkg or .nuspec files.
    /// </summary>
    public sealed class ChocolateySource : ISource {
        /// <inheritdoc />
        public string Name => nameof(ChocolateySource);

        private static readonly string? ChocolateyRoot = GetChocolateyRoot();

        private static string? GetChocolateyRoot() {
            // Prefer ChocolateyInstall env var
            var chocoEnv = Environment.GetEnvironmentVariable("ChocolateyInstall");
            if (!string.IsNullOrEmpty(chocoEnv) && Directory.Exists(chocoEnv)) {
                return chocoEnv;
            }

            // Default location
            const string defaultPath = @"C:\ProgramData\chocolatey";
            return Directory.Exists(defaultPath) ? defaultPath : null;
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<AppHit> QueryAsync(
            string query,
            SourceOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {
            await Task.Yield();

            if (string.IsNullOrWhiteSpace(query) || ChocolateyRoot == null) {
                yield break;
            }

            // Chocolatey is always machine-scope
            if (options.UserOnly) {
                yield break;
            }

            var normalizedQuery = query.ToLowerInvariant();
            var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var libDir = Path.Combine(ChocolateyRoot, "lib");
            if (!Directory.Exists(libDir)) {
                yield break;
            }

            string[] packageDirs;
            try {
                packageDirs = Directory.GetDirectories(libDir);
            }
            catch {
                yield break;
            }

            foreach (var packageDir in packageDirs) {
                if (ct.IsCancellationRequested) {
                    yield break;
                }

                var packageName = Path.GetFileName(packageDir);
                var packageNameLower = packageName.ToLowerInvariant();

                // Skip special directories
                if (packageNameLower is "_processed" or ".chocolatey") {
                    continue;
                }

                // Match check
                if (!MatchesQuery(packageNameLower, normalizedQuery, queryTokens)) {
                    continue;
                }

                // Read nuspec for metadata
                var (version, title, exeHints) = ReadNuspec(packageDir, packageName);

                // Also check title for match if we have one
                if (!string.IsNullOrEmpty(title)) {
                    var titleLower = title.ToLowerInvariant();
                    if (!MatchesQuery(packageNameLower, normalizedQuery, queryTokens) &&
                        !MatchesQuery(titleLower, normalizedQuery, queryTokens)) {
                        continue;
                    }
                }

                var evidence = options.IncludeEvidence
                    ? new Dictionary<string, string> {
                        ["ChocoPackage"] = packageName,
                        ["ChocoRoot"] = ChocolateyRoot
                    }
                    : null;

                if (!string.IsNullOrEmpty(title) && options.IncludeEvidence) {
                    evidence!["Title"] = title;
                }

                // The package directory contains the actual app files (usually)
                var toolsDir = Path.Combine(packageDir, "tools");
                var hasToolsDir = Directory.Exists(toolsDir);

                // Emit install_dir hit for package root
                yield return new AppHit(
                    Type: HitType.InstallDir,
                    Scope: Scope.Machine,
                    Path: PathUtils.NormalizePath(packageDir) ?? packageDir,
                    Version: version,
                    PackageType: PackageType.Chocolatey,
                    Source: [Name],
                    Confidence: 0f,
                    Evidence: evidence
                );

                // Find executables
                var exePaths = new List<string>();

                // Check shims directory for this package's executables
                var shimsDir = Path.Combine(ChocolateyRoot, "bin");
                if (Directory.Exists(shimsDir)) {
                    foreach (var shimExe in SafeGetFiles(shimsDir, "*.exe")) {
                        // Check if shim points to this package
                        var shimIgnore = shimExe + ".ignore";
                        if (File.Exists(shimIgnore)) {
                            continue;
                        }

                        var shimName = Path.GetFileNameWithoutExtension(shimExe).ToLowerInvariant();
                        if (shimName.Contains(packageNameLower) || packageNameLower.Contains(shimName)) {
                            exePaths.Add(shimExe);
                        }
                    }
                }

                // Also scan package directory for .exe files
                if (hasToolsDir) {
                    exePaths.AddRange(SafeGetFiles(toolsDir, "*.exe"));
                }

                // Check manifest hints
                foreach (var hint in exeHints) {
                    var fullPath = Path.Combine(packageDir, hint.Replace('/', '\\'));
                    if (File.Exists(fullPath) && !exePaths.Contains(fullPath)) {
                        exePaths.Add(fullPath);
                    }
                }

                foreach (var exe in exePaths.Distinct()) {
                    if (ct.IsCancellationRequested) {
                        yield break;
                    }

                    var exeEvidence = options.IncludeEvidence
                        ? new Dictionary<string, string>(evidence!) { ["ExeName"] = Path.GetFileName(exe) }
                        : null;

                    yield return new AppHit(
                        Type: HitType.Exe,
                        Scope: Scope.Machine,
                        Path: PathUtils.NormalizePath(exe) ?? exe,
                        Version: version,
                        PackageType: PackageType.Chocolatey,
                        Source: [Name],
                        Confidence: 0f,
                        Evidence: exeEvidence
                    );
                }

                // Check for config in .chocolatey subdirectory
                var chocoMetaDir = Path.Combine(packageDir, ".chocolatey");
                if (Directory.Exists(chocoMetaDir)) {
                    var configEvidence = options.IncludeEvidence
                        ? new Dictionary<string, string>(evidence!) { ["MetaDir"] = "true" }
                        : null;

                    yield return new AppHit(
                        Type: HitType.Config,
                        Scope: Scope.Machine,
                        Path: PathUtils.NormalizePath(chocoMetaDir) ?? chocoMetaDir,
                        Version: version,
                        PackageType: PackageType.Chocolatey,
                        Source: [Name],
                        Confidence: 0f,
                        Evidence: configEvidence
                    );
                }
            }
        }

        private static bool MatchesQuery(string nameLower, string normalizedQuery, string[] queryTokens) =>
            nameLower.Contains(normalizedQuery) || queryTokens.All(nameLower.Contains) || normalizedQuery.Contains(nameLower);

        private static (string? Version, string? Title, List<string> ExeHints) ReadNuspec(string packageDir, string packageName) {
            // Look for .nuspec file
            var nuspecPath = Path.Combine(packageDir, $"{packageName}.nuspec");
            if (!File.Exists(nuspecPath)) {
                // Try .chocolatey subdirectory
                var chocoDir = Path.Combine(packageDir, ".chocolatey");
                if (Directory.Exists(chocoDir)) {
                    var nuspecFiles = SafeGetFiles(chocoDir, "*.nuspec");
                    if (nuspecFiles.Length > 0) {
                        nuspecPath = nuspecFiles[0];
                    }
                }
            }

            if (!File.Exists(nuspecPath)) {
                return (null, null, []);
            }

            try {
                var doc = XDocument.Load(nuspecPath);
                var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
                var metadata = doc.Root?.Element(ns + "metadata");

                var version = metadata?.Element(ns + "version")?.Value;
                var title = metadata?.Element(ns + "title")?.Value;

                // Chocolatey nuspec doesn't typically have exe hints, but check files element
                var exeHints = new List<string>();
                var files = doc.Root?.Element(ns + "files");
                if (files != null) {
                    foreach (var file in files.Elements(ns + "file")) {
                        var src = file.Attribute("src")?.Value;
                        if (!string.IsNullOrEmpty(src) && src.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                            exeHints.Add(src);
                        }
                    }
                }

                return (version, title, exeHints);
            }
            catch {
                return (null, null, []);
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
