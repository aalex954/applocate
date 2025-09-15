using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Win32;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources;

/// <summary>Enumerates App Paths registry keys to resolve executables and their directories.</summary>
/// <summary>
/// Queries HKCU/HKLM App Paths registry keys to surface executable locations registered via the legacy App Paths mechanism.
/// Provides strong signals for primary executables and their containing install directories.
/// </summary>
public sealed class AppPathsSource : ISource
{
    /// <inheritdoc />
    public string Name => "AppPaths";

    private static readonly string[] RootsMachine =
    [
        @"HKEY_LOCAL_MACHINE\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths"
    ];
    private static readonly string[] RootsUser =
    [
        @"HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths"
    ];

    /// <inheritdoc />
    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        if (string.IsNullOrWhiteSpace(query)) { yield break; }
        var norm = query.ToLowerInvariant();
        if (!options.MachineOnly)
        {
            foreach (var hit in Enumerate(RootsUser, Scope.User, norm, options, ct)) { yield return hit; }
        }
        if (!options.UserOnly)
        {
            foreach (var hit in Enumerate(RootsMachine, Scope.Machine, norm, options, ct)) { yield return hit; }
        }
    }

    private IEnumerable<AppHit> Enumerate(IEnumerable<string> roots, Scope scope, string norm, SourceOptions options, CancellationToken ct)
    {
        foreach (var root in roots)
        {
            if (ct.IsCancellationRequested) { yield break; }
            using var rk = OpenRoot(root);
            if (rk == null) { continue; }
            string[] subs;
            try { subs = rk.GetSubKeyNames(); } catch { continue; }
            foreach (var sub in subs)
            {
                if (ct.IsCancellationRequested) { yield break; }
                using var subKey = rk.OpenSubKey(sub);
                if (subKey == null) { continue; }
                var keyLower = sub.ToLowerInvariant();
                var match = keyLower.Contains(norm);
                if (!match) { continue; }
                string? exePath = null; string? pathDir = null;
                try { exePath = (subKey.GetValue(null) as string)?.Trim().Trim('"'); } catch { }
                try { pathDir = (subKey.GetValue("Path") as string)?.Trim().Trim('"'); } catch { }
                Dictionary<string, string>? evidence = null;
                if (options.IncludeEvidence)
                {
                    evidence = new Dictionary<string, string> { { "Key", sub } };
                    if (!string.IsNullOrEmpty(exePath)) { evidence["HasExe"] = "true"; }
                    if (!string.IsNullOrEmpty(pathDir)) { evidence["HasPath"] = "true"; }
                }
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    yield return new AppHit(HitType.Exe, scope, exePath!, null, PackageType.EXE, new[] { Name }, 0, evidence);
                    var dir = Path.GetDirectoryName(exePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        yield return new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.EXE, new[] { Name }, 0, evidence);
                    }
                }
                if (!string.IsNullOrWhiteSpace(pathDir) && Directory.Exists(pathDir))
                {
                    yield return new AppHit(HitType.InstallDir, scope, pathDir!, null, PackageType.EXE, new[] { Name }, 0, evidence);
                }
            }
        }
    }

    private static RegistryKey? OpenRoot(string path)
    {
        const string HKLM = "HKEY_LOCAL_MACHINE\\";
        const string HKCU = "HKEY_CURRENT_USER\\";
        try
        {
            if (path.StartsWith(HKLM, StringComparison.OrdinalIgnoreCase))
            {
                var sub = path[HKLM.Length..];
                return Registry.LocalMachine.OpenSubKey(sub);
            }
            if (path.StartsWith(HKCU, StringComparison.OrdinalIgnoreCase))
            {
                var sub = path[HKCU.Length..];
                return Registry.CurrentUser.OpenSubKey(sub);
            }
        }
        catch { }
        return null;
    }
}
