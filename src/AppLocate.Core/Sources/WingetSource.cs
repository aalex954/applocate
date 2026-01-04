using System.Diagnostics;
using System.Text.Json;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources;

/// <summary>
/// Discovers applications managed by the Windows Package Manager (winget).
/// Uses "winget export" to get JSON list of installed packages from the winget source.
/// This provides package identifiers and source attribution for apps that may also
/// appear from other sources (registry, Start Menu) with enhanced provenance.
/// </summary>
public sealed class WingetSource : ISource {
    /// <inheritdoc />
    public string Name => nameof(WingetSource);

    private static readonly Lazy<bool> WingetAvailable = new(CheckWingetAvailable);
    private static readonly TimeSpan WingetTimeout = TimeSpan.FromSeconds(15);

    // Cache the winget export result at the static level to avoid repeated expensive CLI calls.
    // This is fine since winget packages don't change during a single applocate session.
    private static List<WingetPackage>? s_cachedPackages;
    private static readonly object s_cacheLock = new();

    private static bool CheckWingetAvailable() {
        try {
            var psi = new ProcessStartInfo("winget", "--version") {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(2000);
            return proc.ExitCode == 0;
        }
        catch {
            return false;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AppHit> QueryAsync(
        string query,
        SourceOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {

        if (string.IsNullOrWhiteSpace(query) || !WingetAvailable.Value) {
            yield break;
        }

        var packages = await GetPackagesAsync();
        if (packages == null || packages.Count == 0) {
            yield break;
        }

        var normalizedQuery = query.ToLowerInvariant();
        var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var pkg in packages) {
            if (ct.IsCancellationRequested) yield break;

            var idLower = pkg.Id.ToLowerInvariant();
            
            // Match against package ID (e.g., "Microsoft.VisualStudioCode")
            // Split on dots for better matching
            var idParts = idLower.Split('.');
            
            if (!MatchesQuery(idLower, idParts, normalizedQuery, queryTokens)) {
                continue;
            }

            // Determine scope - winget packages are typically machine scope
            // but user installs are possible. We'll default to machine.
            var scope = Scope.Machine;
            if (options.UserOnly && scope == Scope.Machine) continue;
            if (options.MachineOnly && scope == Scope.User) continue;

            var evidence = options.IncludeEvidence
                ? new Dictionary<string, string> {
                    ["WingetId"] = pkg.Id,
                    ["WingetSource"] = pkg.Source ?? "winget"
                }
                : null;

            // WinGet doesn't give us paths directly - it just confirms the package is managed.
            // We try to find the actual install location from heuristics.
            // If not found, we still emit a synthetic hit with the package ID as the path marker
            // so that downstream merging can associate WingetId evidence with registry/Start Menu hits.
            
            var installPath = TryResolveInstallPath(pkg.Id);
            
            if (!string.IsNullOrEmpty(installPath)) {
                yield return new AppHit(
                    Type: HitType.InstallDir,
                    Scope: scope,
                    Path: PathUtils.NormalizePath(installPath) ?? installPath,
                    Version: null, // Version would require additional winget call
                    PackageType: PackageType.Winget,
                    Source: [Name],
                    Confidence: 0f,
                    Evidence: evidence
                );

                // Look for exe in install path
                var exes = SafeGetFiles(installPath, "*.exe");
                var mainExe = FindMainExecutable(exes, pkg.Id);
                if (mainExe != null) {
                    var exeEvidence = options.IncludeEvidence
                        ? new Dictionary<string, string>(evidence!) { ["ExeName"] = Path.GetFileName(mainExe) }
                        : null;

                    yield return new AppHit(
                        Type: HitType.Exe,
                        Scope: scope,
                        Path: PathUtils.NormalizePath(mainExe) ?? mainExe,
                        Version: null,
                        PackageType: PackageType.Winget,
                        Source: [Name],
                        Confidence: 0f,
                        Evidence: exeEvidence
                    );
                }
            }
            else {
                // Emit a marker hit that can be used to enhance other sources' hits
                // The ranker/merger will see WingetId evidence and can boost confidence
                // Path is the package ID itself as a placeholder (won't pass existence check)
                yield return new AppHit(
                    Type: HitType.InstallDir,
                    Scope: scope,
                    Path: $"winget://{pkg.Id}",  // Synthetic URI-style path for merging
                    Version: null,
                    PackageType: PackageType.Winget,
                    Source: [Name],
                    Confidence: 0f,
                    Evidence: evidence
                );
            }
        }
    }

    private static async Task<List<WingetPackage>?> GetPackagesAsync() {
        lock (s_cacheLock) {
            if (s_cachedPackages != null) {
                return s_cachedPackages;
            }
        }

        // Note: We pass CancellationToken.None because the per-source timeout
        // (default 5s) is too short for winget export. WingetSource uses its own
        // longer timeout internally.
        var packages = await FetchPackagesFromWingetAsync(CancellationToken.None);
        
        lock (s_cacheLock) {
            s_cachedPackages = packages;
            return s_cachedPackages;
        }
    }

    private static async Task<List<WingetPackage>?> FetchPackagesFromWingetAsync(CancellationToken ct) {
        var tempFile = Path.Combine(Path.GetTempPath(), $"applocate-winget-{Guid.NewGuid():N}.json");
        
        try {
            var psi = new ProcessStartInfo("winget", $"export -o \"{tempFile}\" --disable-interactivity") {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) {
                return null;
            }

            // Must read stdout/stderr to prevent buffer blocking
            // We don't need the output, but we need to drain it
#pragma warning disable CA2016 // CancellationToken not supported by this overload
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
#pragma warning restore CA2016

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(WingetTimeout);

            try {
                await proc.WaitForExitAsync(cts.Token);
                // Ensure streams are drained
                await Task.WhenAll(stdoutTask, stderrTask);
            }
            catch (OperationCanceledException) {
                try { proc.Kill(); } catch { }
                return null;
            }

            if (!File.Exists(tempFile)) {
                return null;
            }

            var json = await File.ReadAllTextAsync(tempFile, ct);
            return ParseWingetExport(json);
        }
        catch {
            return null;
        }
        finally {
            try { File.Delete(tempFile); } catch { }
        }
    }

    private static List<WingetPackage>? ParseWingetExport(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var packages = new List<WingetPackage>();

            if (!root.TryGetProperty("Sources", out var sources)) {
                return packages;
            }

            foreach (var source in sources.EnumerateArray()) {
                string? sourceName = null;
                if (source.TryGetProperty("SourceDetails", out var details) &&
#pragma warning disable CA1507 // JSON property name "Name" doesn't match any C# member
                    details.TryGetProperty("Name", out var nameProp)) {
#pragma warning restore CA1507
                    sourceName = nameProp.GetString();
                }

                if (!source.TryGetProperty("Packages", out var pkgs)) continue;

                foreach (var pkg in pkgs.EnumerateArray()) {
                    if (pkg.TryGetProperty("PackageIdentifier", out var idProp)) {
                        var id = idProp.GetString();
                        if (!string.IsNullOrEmpty(id)) {
                            packages.Add(new WingetPackage(id, sourceName));
                        }
                    }
                }
            }

            return packages;
        }
        catch {
            return null;
        }
    }

    private static bool MatchesQuery(string idLower, string[] idParts, string normalizedQuery, string[] queryTokens) {
        // Direct substring match on full ID
        if (idLower.Contains(normalizedQuery)) return true;

        // Query tokens all present in ID
        if (queryTokens.All(t => idLower.Contains(t))) return true;

        // Match any ID part (e.g., "code" matches "Microsoft.VisualStudioCode")
        if (idParts.Any(p => p.Contains(normalizedQuery) || normalizedQuery.Contains(p))) return true;

        // All query tokens present across ID parts
        if (queryTokens.All(t => idParts.Any(p => p.Contains(t)))) return true;

        return false;
    }

    private static string? TryResolveInstallPath(string packageId) {
        // Common install locations based on package ID patterns
        var parts = packageId.Split('.');
        if (parts.Length < 2) return null;

        var publisher = parts[0];
        var appName = string.Join("", parts.Skip(1));

        // Check Program Files
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new[] {
            Path.Combine(programFiles, publisher, appName),
            Path.Combine(programFiles, appName),
            Path.Combine(programFilesX86, publisher, appName),
            Path.Combine(programFilesX86, appName),
            Path.Combine(localAppData, "Programs", appName),
            Path.Combine(localAppData, publisher, appName),
        };

        foreach (var candidate in candidates) {
            if (Directory.Exists(candidate)) {
                return candidate;
            }
        }

        return null;
    }

    private static string? FindMainExecutable(string[] exes, string packageId) {
        if (exes.Length == 0) return null;
        if (exes.Length == 1) return exes[0];

        var idParts = packageId.ToLowerInvariant().Split('.');
        var appName = idParts.LastOrDefault() ?? "";

        // Prefer exe that matches app name
        var match = exes.FirstOrDefault(e => 
            Path.GetFileNameWithoutExtension(e).Equals(appName, StringComparison.OrdinalIgnoreCase));
        
        if (match != null) return match;

        // Prefer shortest name (often the main exe)
        return exes.OrderBy(e => Path.GetFileName(e).Length).First();
    }

    private static string[] SafeGetFiles(string path, string pattern) {
        try { return Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly); }
        catch { return []; }
    }

    private sealed record WingetPackage(string Id, string? Source);
}
