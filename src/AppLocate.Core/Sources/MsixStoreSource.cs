using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources;

/// <summary>Enumerates MSIX / Store packages (PowerShell or env injected) to produce install dir and exe hits.</summary>
public sealed class MsixStoreSource : ISource
{
    /// <summary>Unique source identifier used in evidence arrays.</summary>
    public string Name => nameof(MsixStoreSource);

    internal interface IMsixPackageProvider
    {
        IEnumerable<(string name,string family,string install,string version)> Enumerate();
    }
    private sealed class PowerShellMsixProvider : IMsixPackageProvider
    {
        public IEnumerable<(string name,string family,string install,string version)> Enumerate()
        {
            var packages = new List<(string,string,string,string)>();
            try
            {
                using var p = new System.Diagnostics.Process();
                p.StartInfo.FileName = Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe");
                p.StartInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"$ErrorActionPreference='SilentlyContinue'; Get-AppxPackage | ForEach-Object { \"$($_.Name)|$($_.PackageFamilyName)|$($_.InstallLocation)|$($_.Version)\" }\"";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                while (!p.StandardOutput.EndOfStream)
                {
                    var line = p.StandardOutput.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('|');
                    if (parts.Length < 4) continue;
                    packages.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), parts[3].Trim()));
                }
                try { if (!p.HasExited) p.Kill(true); } catch { }
            }
            catch { }
            return packages;
        }
    }
    private sealed class EnvMsixProvider : IMsixPackageProvider
    {
        public IEnumerable<(string name,string family,string install,string version)> Enumerate()
        {
            var json = Environment.GetEnvironmentVariable("APPLOCATE_MSIX_FAKE");
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<(string,string,string,string)>();
            try
            {
                var arr = System.Text.Json.JsonDocument.Parse(json).RootElement;
                if (arr.ValueKind != System.Text.Json.JsonValueKind.Array) return Array.Empty<(string,string,string,string)>();
                var list = new List<(string,string,string,string)>();
                foreach (var el in arr.EnumerateArray())
                {
                    var name = el.GetProperty("name").GetString() ?? string.Empty;
                    var family = el.GetProperty("family").GetString() ?? name + ".fake";
                    var install = el.GetProperty("install").GetString() ?? string.Empty;
                    var version = el.TryGetProperty("version", out var v) ? (v.GetString() ?? "") : "1.0.0.0";
                    if (name.Length == 0 || install.Length == 0) continue;
                    list.Add((name,family,install,version));
                }
                return list;
            }
            catch { return Array.Empty<(string,string,string,string)>(); }
        }
    }
    private static IMsixPackageProvider CreateProvider()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLOCATE_MSIX_FAKE"))) return new EnvMsixProvider();
        return new PowerShellMsixProvider();
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
    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await System.Threading.Tasks.Task.Yield();
        if (string.IsNullOrWhiteSpace(query)) yield break;
        var norm = query.ToLowerInvariant();
        var tokens = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var provider = CreateProvider();
        var packages = provider.Enumerate().ToList();
        if (packages.Count == 0) yield break;
        var seenInstall = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pkg in packages)
        {
            if (ct.IsCancellationRequested) yield break;
            bool match;
            var nameLower = pkg.name.ToLowerInvariant();
            var famLower = pkg.family.ToLowerInvariant();
            if (options.Strict)
            {
                match = tokens.All(t => nameLower.Contains(t) || famLower.Contains(t));
            }
            else
            {
                match = nameLower.Contains(norm) || famLower.Contains(norm);
            }
            if (!match) continue;
            var scope = Scope.User;
            if (options.MachineOnly) continue;

            if (!string.IsNullOrWhiteSpace(pkg.install) && Directory.Exists(pkg.install) && seenInstall.Add(pkg.install))
            {
                Dictionary<string,string>? evidence = null;
                if (options.IncludeEvidence)
                {
                    evidence = new Dictionary<string,string>{{EvidenceKeys.PackageFamilyName, pkg.family},{EvidenceKeys.PackageName, pkg.name}};
                    if (!string.IsNullOrEmpty(pkg.version)) evidence[EvidenceKeys.PackageVersion] = pkg.version;
                }
                yield return new AppHit(HitType.InstallDir, scope, pkg.install, pkg.version, PackageType.MSIX, new[] { Name }, 0, evidence);

                IEnumerable<string> exes = Array.Empty<string>();
                try { exes = Directory.EnumerateFiles(pkg.install, "*.exe", SearchOption.TopDirectoryOnly); } catch { }
                foreach (var exe in exes)
                {
                    if (ct.IsCancellationRequested) yield break;
                    if (!File.Exists(exe)) continue;
                    var exeNameLower = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                    bool exeMatch = options.Strict ? tokens.All(t => exeNameLower.Contains(t)) : exeNameLower.Contains(norm) || match;
                    if (!exeMatch) continue;
                    if (!seenExe.Add(exe)) continue;
                    Dictionary<string,string>? exeEvidence = evidence;
                    if (options.IncludeEvidence)
                        exeEvidence = new Dictionary<string,string>(evidence ?? new()) {{EvidenceKeys.ExeName, Path.GetFileName(exe)}};
                    yield return new AppHit(HitType.Exe, scope, exe, pkg.version, PackageType.MSIX, new[] { Name }, 0, exeEvidence);
                }
            }
        }
    }
}
