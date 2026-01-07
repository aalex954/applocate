using System.Text.Json;
using System.Xml.Linq;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources {
    /// <summary>
    /// Provider abstraction for Chocolatey filesystem operations.
    /// Enables deterministic testing without real Chocolatey installation.
    /// </summary>
    public interface IChocolateyProvider {
        /// <summary>Gets the Chocolatey root path, or null if not found.</summary>
        string? GetRoot();

        /// <summary>Checks if a directory exists.</summary>
        bool DirectoryExists(string path);

        /// <summary>Gets subdirectories of a directory.</summary>
        string[] GetDirectories(string path);

        /// <summary>Gets files matching a pattern in a directory.</summary>
        string[] GetFiles(string path, string pattern);

        /// <summary>Checks if a file exists.</summary>
        bool FileExists(string path);

        /// <summary>Reads all text from a file, or returns null if the file doesn't exist.</summary>
        string? ReadFileText(string path);

        /// <summary>Loads an XDocument from a file, or returns null if not found or parse error.</summary>
        XDocument? LoadXDocument(string path);
    }

    /// <summary>
    /// Real filesystem provider for Chocolatey.
    /// </summary>
    public sealed class FileSystemChocolateyProvider : IChocolateyProvider {
        /// <inheritdoc />
        public string? GetRoot() {
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
        public bool DirectoryExists(string path) => Directory.Exists(path);

        /// <inheritdoc />
        public string[] GetDirectories(string path) {
            try {
                return Directory.GetDirectories(path);
            }
            catch {
                return [];
            }
        }

        /// <inheritdoc />
        public string[] GetFiles(string path, string pattern) {
            try {
                return Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly);
            }
            catch {
                return [];
            }
        }

        /// <inheritdoc />
        public bool FileExists(string path) => File.Exists(path);

        /// <inheritdoc />
        public string? ReadFileText(string path) {
            try {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch {
                return null;
            }
        }

        /// <inheritdoc />
        public XDocument? LoadXDocument(string path) {
            try {
                return File.Exists(path) ? XDocument.Load(path) : null;
            }
            catch {
                return null;
            }
        }
    }

    /// <summary>
    /// Fake provider for deterministic testing.
    /// Reads fixture data from APPLOCATE_CHOCO_FAKE environment variable (JSON).
    /// </summary>
    public sealed class FakeChocolateyProvider : IChocolateyProvider {
        private readonly string _root;
        private readonly HashSet<string> _directories;
        private readonly Dictionary<string, List<string>> _directoryContents;
        private readonly Dictionary<string, List<string>> _filesByPattern;
        private readonly Dictionary<string, string> _fileContents;
        private readonly Dictionary<string, string> _xmlContents;

        /// <summary>
        /// Creates a FakeChocolateyProvider from JSON fixture data.
        /// </summary>
        /// <param name="json">JSON string containing fixture data.</param>
        public FakeChocolateyProvider(string json) {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _root = root.GetProperty("root").GetString() ?? @"C:\ProgramData\chocolatey";
            _directories = [];
            _directoryContents = [];
            _filesByPattern = [];
            _fileContents = [];
            _xmlContents = [];

            // Parse directories
            if (root.TryGetProperty("directories", out var dirsElem)) {
                foreach (var dir in dirsElem.EnumerateArray()) {
                    _directories.Add(dir.GetString()!);
                }
            }

            // Parse directory contents (subdirectories)
            if (root.TryGetProperty("directoryContents", out var dcElem)) {
                foreach (var prop in dcElem.EnumerateObject()) {
                    var items = prop.Value.EnumerateArray().Select(x => x.GetString()!).ToList();
                    _directoryContents[prop.Name] = items;
                }
            }

            // Parse files by pattern
            if (root.TryGetProperty("filesByPattern", out var fbpElem)) {
                foreach (var prop in fbpElem.EnumerateObject()) {
                    var items = prop.Value.EnumerateArray().Select(x => x.GetString()!).ToList();
                    _filesByPattern[prop.Name] = items;
                }
            }

            // Parse file contents
            if (root.TryGetProperty("fileContents", out var fcElem)) {
                foreach (var prop in fcElem.EnumerateObject()) {
                    _fileContents[prop.Name] = prop.Value.GetString()!;
                }
            }

            // Parse XML contents
            if (root.TryGetProperty("xmlContents", out var xcElem)) {
                foreach (var prop in xcElem.EnumerateObject()) {
                    _xmlContents[prop.Name] = prop.Value.GetString()!;
                }
            }
        }

        /// <inheritdoc />
        public string? GetRoot() => _root;

        /// <inheritdoc />
        public bool DirectoryExists(string path) => _directories.Contains(path);

        /// <inheritdoc />
        public string[] GetDirectories(string path) =>
            _directoryContents.TryGetValue(path, out var dirs) ? [.. dirs] : [];

        /// <inheritdoc />
        public string[] GetFiles(string path, string pattern) {
            var key = $"{path}|{pattern}";
            return _filesByPattern.TryGetValue(key, out var files) ? [.. files] : [];
        }

        /// <inheritdoc />
        public bool FileExists(string path) => _fileContents.ContainsKey(path) || _xmlContents.ContainsKey(path);

        /// <inheritdoc />
        public string? ReadFileText(string path) =>
            _fileContents.TryGetValue(path, out var content) ? content : null;

        /// <inheritdoc />
        public XDocument? LoadXDocument(string path) {
            if (!_xmlContents.TryGetValue(path, out var xml)) {
                return null;
            }

            try {
                return XDocument.Parse(xml);
            }
            catch {
                return null;
            }
        }
    }

    /// <summary>
    /// Discovers applications installed via the Chocolatey package manager.
    /// Chocolatey installs to C:\ProgramData\chocolatey by default.
    /// Package metadata is in lib\{package}\.chocolatey\{package}.{version}\.nupkg or .nuspec files.
    /// </summary>
    public sealed class ChocolateySource : ISource {
        /// <inheritdoc />
        public string Name => nameof(ChocolateySource);

        private readonly IChocolateyProvider _provider;

        /// <summary>
        /// Creates a ChocolateySource with the specified provider.
        /// </summary>
        public ChocolateySource(IChocolateyProvider provider) {
            _provider = provider;
        }

        /// <summary>
        /// Creates a ChocolateySource with a provider based on environment configuration.
        /// Uses FakeChocolateyProvider if APPLOCATE_CHOCO_FAKE is set, otherwise FileSystemChocolateyProvider.
        /// </summary>
        public ChocolateySource() {
            var fakeJson = Environment.GetEnvironmentVariable("APPLOCATE_CHOCO_FAKE");
            _provider = string.IsNullOrEmpty(fakeJson)
                ? new FileSystemChocolateyProvider()
                : new FakeChocolateyProvider(fakeJson);
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<AppHit> QueryAsync(
            string query,
            SourceOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {
            await Task.Yield();

            var chocolateyRoot = _provider.GetRoot();
            if (string.IsNullOrWhiteSpace(query) || chocolateyRoot == null) {
                yield break;
            }

            // Chocolatey is always machine-scope
            if (options.UserOnly) {
                yield break;
            }

            var normalizedQuery = query.ToLowerInvariant();
            var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var libDir = Path.Combine(chocolateyRoot, "lib");
            if (!_provider.DirectoryExists(libDir)) {
                yield break;
            }

            var packageDirs = _provider.GetDirectories(libDir);
            if (packageDirs.Length == 0) {
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
                        ["ChocoRoot"] = chocolateyRoot
                    }
                    : null;

                if (!string.IsNullOrEmpty(title) && options.IncludeEvidence) {
                    evidence!["Title"] = title;
                }

                // The package directory contains the actual app files (usually)
                var toolsDir = Path.Combine(packageDir, "tools");
                var hasToolsDir = _provider.DirectoryExists(toolsDir);

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
                var shimsDir = Path.Combine(chocolateyRoot, "bin");
                if (_provider.DirectoryExists(shimsDir)) {
                    foreach (var shimExe in _provider.GetFiles(shimsDir, "*.exe")) {
                        // Check if shim points to this package
                        var shimIgnore = shimExe + ".ignore";
                        if (_provider.FileExists(shimIgnore)) {
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
                    exePaths.AddRange(_provider.GetFiles(toolsDir, "*.exe"));
                }

                // Check manifest hints
                foreach (var hint in exeHints) {
                    var fullPath = Path.Combine(packageDir, hint.Replace('/', '\\'));
                    if (_provider.FileExists(fullPath) && !exePaths.Contains(fullPath)) {
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
                if (_provider.DirectoryExists(chocoMetaDir)) {
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

        private (string? Version, string? Title, List<string> ExeHints) ReadNuspec(string packageDir, string packageName) {
            // Look for .nuspec file
            var nuspecPath = Path.Combine(packageDir, $"{packageName}.nuspec");

            // Try to load from primary path
            var doc = _provider.LoadXDocument(nuspecPath);

            if (doc == null) {
                // Try .chocolatey subdirectory
                var chocoDir = Path.Combine(packageDir, ".chocolatey");
                if (_provider.DirectoryExists(chocoDir)) {
                    var nuspecFiles = _provider.GetFiles(chocoDir, "*.nuspec");
                    if (nuspecFiles.Length > 0) {
                        doc = _provider.LoadXDocument(nuspecFiles[0]);
                    }
                }
            }

            if (doc == null) {
                return (null, null, []);
            }

            try {
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
    }
}
