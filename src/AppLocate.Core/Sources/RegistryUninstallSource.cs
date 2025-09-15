using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Win32;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources;

/// <summary>Enumerates uninstall registry keys to infer install directories and primary executables.</summary>
public sealed class RegistryUninstallSource : ISource
{
    /// <summary>Source name.</summary>
    public string Name => nameof(RegistryUninstallSource);

    // NOTE: Single backslashes only. Previously these literals had doubled backslashes which caused
    // the computed sub-path in OpenRoot to start with a leading '\\' and fail to open the key, breaking HKCU tests.
    private static readonly string[] UninstallRootsMachine =
    [
        @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];
    private static readonly string[] UninstallRootsUser =
    [
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    /// <inheritdoc />
    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await System.Threading.Tasks.Task.Yield();
        if (string.IsNullOrWhiteSpace(query)) yield break;
        var list = new List<AppHit>();
        var normalized = query.ToLowerInvariant();
        if (!options.MachineOnly)
            EnumerateRoots(UninstallRootsUser, Scope.User, normalized, options, list, ct);
        if (!options.UserOnly)
            EnumerateRoots(UninstallRootsMachine, Scope.Machine, normalized, options, list, ct);
        foreach (var h in list) yield return h;
    }

    private void EnumerateRoots(IEnumerable<string> roots, Scope scope, string normalizedQuery, SourceOptions options, List<AppHit> sink, CancellationToken ct)
    {
        var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var root in roots)
        {
            if (ct.IsCancellationRequested) return;
            RegistryKey? rk = null;
            try { rk = OpenRoot(root); } catch { }
            if (rk == null) continue;
            using (rk)
            {
                string[] subNames;
                try { subNames = rk.GetSubKeyNames(); }
                catch { continue; }
                foreach (var sub in subNames)
                {
                    if (ct.IsCancellationRequested) return;
                    try
                    {
                        using var subKey = rk.OpenSubKey(sub);
                        if (subKey == null) continue;
                        var displayName = (subKey.GetValue("DisplayName") as string)?.Trim();
                        var dnLower = displayName?.ToLowerInvariant();
                        var keyLower = sub.ToLowerInvariant();
                        if (string.IsNullOrEmpty(displayName)) continue;

                        if (options.Strict)
                        {
                            if (!AllTokensPresent(dnLower!, queryTokens)) continue;
                        }
                        else
                        {
                            if (!dnLower!.Contains(normalizedQuery) && !keyLower.Contains(normalizedQuery)) continue;
                        }

                        var installLocation = (subKey.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                        if (string.IsNullOrEmpty(installLocation)) installLocation = null;
                        var displayIconRaw = (subKey.GetValue("DisplayIcon") as string)?.Trim();
                        var version = (subKey.GetValue("DisplayVersion") as string)?.Trim();
                        var windowsInstaller = (subKey.GetValue("WindowsInstaller") as int?) == 1 || (subKey.GetValue("WindowsInstaller") as string) == "1";

                        string? exeCandidate = ParseExeFromDisplayIcon(displayIconRaw);
                        if (installLocation == null && exeCandidate != null)
                        {
                            try { installLocation = Path.GetDirectoryName(exeCandidate); } catch { }
                        }

                        var pkgType = windowsInstaller ? PackageType.MSI : PackageType.EXE;

                        Dictionary<string,string>? evidence = null;
                        if (options.IncludeEvidence)
                        {
                            evidence = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
                            {
                                {"DisplayName", displayName!},
                                {"Key", sub}
                            };
                            if (windowsInstaller) evidence["WindowsInstaller"] = "1";
                            if (installLocation != null) evidence["HasInstallLocation"] = "true";
                            if (!string.IsNullOrEmpty(displayIconRaw)) evidence["HasDisplayIcon"] = "true";
                        }

                        if (installLocation != null && PathIsPlausibleDir(installLocation))
                        {
                            sink.Add(new AppHit(HitType.InstallDir, scope, installLocation, version, pkgType, new[] { Name }, 0, evidence));
                        }
                        if (exeCandidate != null && File.Exists(exeCandidate))
                        {
                            sink.Add(new AppHit(HitType.Exe, scope, exeCandidate, version, pkgType, new[] { Name }, 0, evidence));
                        }
                    }
                    catch { }
                }
            }
        }
    }

    private static bool AllTokensPresent(string displayNameLower, string[] queryTokens)
    {
        foreach (var t in queryTokens)
        {
            if (!displayNameLower.Contains(t)) return false;
        }
        return true;
    }

    private static bool PathIsPlausibleDir(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var lower = path.ToLowerInvariant();
            if (lower.Contains("\\uninstall") || lower.Contains("%temp%")) return false;
            return true;
        }
        catch { return false; }
    }

    private static string? ParseExeFromDisplayIcon(string? displayIconRaw)
    {
        if (string.IsNullOrWhiteSpace(displayIconRaw)) return null;
        var s = displayIconRaw.Trim().Trim('"');
        var commaIdx = s.IndexOf(',');
        if (commaIdx > 0) s = s.Substring(0, commaIdx);
        s = s.Trim();
        if (!s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        var fileLower = Path.GetFileName(s).ToLowerInvariant();
        if (fileLower is "uninstall.exe" or "unins000.exe" or "setup.exe" or "msiexec.exe" or "_uninst.exe") return null;
        return s;
    }

    private static RegistryKey? OpenRoot(string path)
    {
        const string HKLM = "HKEY_LOCAL_MACHINE\\";
        const string HKCU = "HKEY_CURRENT_USER\\";
        try
        {
            if (path.StartsWith(HKLM, StringComparison.OrdinalIgnoreCase))
            {
                var sub = path.Substring(HKLM.Length);
                return Registry.LocalMachine.OpenSubKey(sub);
            }
            if (path.StartsWith(HKCU, StringComparison.OrdinalIgnoreCase))
            {
                var sub = path.Substring(HKCU.Length);
                return Registry.CurrentUser.OpenSubKey(sub);
            }
        }
        catch { }
        return null;
    }
}
