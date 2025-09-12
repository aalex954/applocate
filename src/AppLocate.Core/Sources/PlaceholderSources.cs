using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Win32;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources;

// These placeholder source implementations return no results. They will be replaced incrementally.
/// <summary>Enumerates uninstall registry keys to infer install directories and primary executables.</summary>
public sealed class RegistryUninstallSource : ISource
{
    /// <summary>Source name.</summary>
    public string Name => nameof(RegistryUninstallSource);

    private static readonly string[] UninstallRootsMachine =
    [
        @"HKEY_LOCAL_MACHINE\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
        @"HKEY_LOCAL_MACHINE\\Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
    ];
    private static readonly string[] UninstallRootsUser =
    [
        @"HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
    ];

    /// <summary>Queries registry uninstall keys for apps whose DisplayName contains the normalized query.</summary>
    /// <summary>Enumerates Start Menu shortcuts matching the normalized query and resolves their target executables.</summary>
    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Very small first pass: linear scan of uninstall keys, substring match on DisplayName.
        await System.Threading.Tasks.Task.Yield();
        var hits = new List<AppHit>();
        if (!options.MachineOnly)
            EnumerateRoots(UninstallRootsUser, Scope.User, query, options, hits, ct);
        if (!options.UserOnly)
            EnumerateRoots(UninstallRootsMachine, Scope.Machine, query, options, hits, ct);
        foreach (var h in hits) yield return h;
    }

    private void EnumerateRoots(IEnumerable<string> roots, Scope scope, string query, SourceOptions options, List<AppHit> sink, CancellationToken ct)
    {
        foreach (var root in roots)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                using var rk = OpenRoot(root);
                if (rk == null) continue;
                foreach (var sub in rk.GetSubKeyNames())
                {
                    if (ct.IsCancellationRequested) return;
                    try
                    {
                        using var subKey = rk.OpenSubKey(sub);
                        if (subKey == null) continue;
                        var displayName = subKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;
                        var norm = displayName.ToLowerInvariant();
                        if (!norm.Contains(query)) continue;
                        var installLocation = (subKey.GetValue("InstallLocation") as string)?.Trim();
                        var displayIcon = (subKey.GetValue("DisplayIcon") as string)?.Trim('"');
                        var version = subKey.GetValue("DisplayVersion") as string;
                            var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{"DisplayName", displayName}} : null;
                            if (!string.IsNullOrEmpty(installLocation))
                                sink.Add(new AppHit(HitType.InstallDir, scope, installLocation!, version, PackageType.MSI, new[] { this.Name }, 0, evidence));
                            if (!string.IsNullOrEmpty(displayIcon) && displayIcon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                sink.Add(new AppHit(HitType.Exe, scope, displayIcon!, version, PackageType.MSI, new[] { this.Name }, 0, evidence));
                    }
                    catch { /* swallow individual key errors */ }
                }
            }
            catch { /* swallow root errors */ }
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

/// <summary>Placeholder: future enumeration of App Paths registry keys.</summary>
public sealed class AppPathsSource : ISource
{
    /// <summary>Source name.</summary>
    public string Name => nameof(AppPathsSource);

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
        await System.Threading.Tasks.Task.Yield();
        var lower = query;
        foreach (var root in RootsUser)
        {
            if (options.MachineOnly) break;
            foreach (var hit in Enumerate(root, Scope.User, lower, options, ct)) yield return hit;
        }
        foreach (var root in RootsMachine)
        {
            if (options.UserOnly) break;
            foreach (var hit in Enumerate(root, Scope.Machine, lower, options, ct)) yield return hit;
        }
    }

    private IEnumerable<AppHit> Enumerate(string rootPath, Scope scope, string query, SourceOptions options, CancellationToken ct)
    {
        using var root = RegistryUninstallSource_OpenRoot(rootPath);
        if (root == null) yield break;
        foreach (var sub in root.GetSubKeyNames())
        {
            if (ct.IsCancellationRequested) yield break;
            List<AppHit>? buffered = null;
            try
            {
                using var subKey = root.OpenSubKey(sub);
                if (subKey == null) continue;
                var keyName = sub.ToLowerInvariant();
                if (!keyName.Contains(query)) continue;
                var exePath = (subKey.GetValue(null) as string)?.Trim('"');
                var pathDir = (subKey.GetValue("Path") as string)?.Trim();
                var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{"Key", sub}} : null;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    buffered ??= new List<AppHit>();
                    buffered.Add(new AppHit(HitType.Exe, scope, exePath!, null, PackageType.EXE, new[] { Name }, 0, evidence));
                }
                if (!string.IsNullOrWhiteSpace(pathDir))
                {
                    buffered ??= new List<AppHit>();
                    buffered.Add(new AppHit(HitType.InstallDir, scope, pathDir!, null, PackageType.EXE, new[] { Name }, 0, evidence));
                }
            }
            catch { }
            if (buffered != null)
            {
                foreach (var h in buffered)
                    yield return h;
            }
        }
    }

    // Reuse helper logic to open HKLM/HKCU root (internal copy of RegistryUninstallSource.OpenRoot logic)
    private static Microsoft.Win32.RegistryKey? RegistryUninstallSource_OpenRoot(string path)
    {
        const string HKLM = "HKEY_LOCAL_MACHINE\\";
        const string HKCU = "HKEY_CURRENT_USER\\";
        try
        {
            if (path.StartsWith(HKLM, StringComparison.OrdinalIgnoreCase))
            {
                var sub = path.Substring(HKLM.Length);
                return Microsoft.Win32.Registry.LocalMachine.OpenSubKey(sub);
            }
            if (path.StartsWith(HKCU, StringComparison.OrdinalIgnoreCase))
            {
                var sub = path.Substring(HKCU.Length);
                return Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sub);
            }
        }
        catch { }
        return null;
    }
}

/// <summary>Placeholder: future enumeration of Start Menu .lnk shortcuts.</summary>
public sealed class StartMenuShortcutSource : ISource
{
    /// <summary>Source name.</summary>
    public string Name => nameof(StartMenuShortcutSource);
    private static readonly string[] StartMenuRootsUser =
    {
        Environment.ExpandEnvironmentVariables("%AppData%\\Microsoft\\Windows\\Start Menu\\Programs")
    };
    private static readonly string[] StartMenuRootsCommon =
    {
        Environment.ExpandEnvironmentVariables("%ProgramData%\\Microsoft\\Windows\\Start Menu\\Programs").Replace("\n", string.Empty)
    };

    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await System.Threading.Tasks.Task.Yield();
        var norm = query;
        if (!options.MachineOnly)
        {
            foreach (var r in Enumerate(StartMenuRootsUser, Scope.User, norm, options, ct)) yield return r;
        }
        if (!options.UserOnly)
        {
            foreach (var r in Enumerate(StartMenuRootsCommon, Scope.Machine, norm, options, ct)) yield return r;
        }
    }

    private IEnumerable<AppHit> Enumerate(IEnumerable<string> roots, Scope scope, string query, SourceOptions options, CancellationToken ct)
    {
        foreach (var root in roots)
        {
            if (ct.IsCancellationRequested) yield break;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            IEnumerable<string> files = Array.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch { }
            foreach (var lnk in files)
            {
                if (ct.IsCancellationRequested) yield break;
                var fileName = Path.GetFileNameWithoutExtension(lnk).ToLowerInvariant();
                if (!fileName.Contains(query)) continue;
                string? target = null;
                try { target = ResolveShortcut(lnk); } catch { }
                if (string.IsNullOrWhiteSpace(target)) continue;
                if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{"Shortcut", lnk}} : null;
                yield return new AppHit(HitType.Exe, scope, target, null, PackageType.EXE, new[] { Name }, 0, evidence);
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir))
                    yield return new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.EXE, new[] { Name }, 0, evidence);
            }
        }
    }

    // Minimal COM-based .lnk resolution using WScript.Shell to avoid adding dependencies at this stage.
    private static string? ResolveShortcut(string lnk)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic sc = shell.CreateShortcut(lnk);
                string? target = sc.TargetPath as string;
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sc);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                return target;
            }
            catch
            {
                try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); } catch { }
            }
        }
        catch { }
        return null;
    }
}

/// <summary>Placeholder: future enumeration of running processes.</summary>
public sealed class ProcessSource : ISource
{
    /// <summary>Source name.</summary>
    public string Name => nameof(ProcessSource);
    /// <inheritdoc />
    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    { await System.Threading.Tasks.Task.CompletedTask; yield break; }
}

/// <summary>Placeholder: future PATH search using where.exe and directory scan.</summary>
public sealed class PathSearchSource : ISource
{
    /// <summary>Source name.</summary>
    public string Name => nameof(PathSearchSource);
    /// <inheritdoc />
    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    { await System.Threading.Tasks.Task.CompletedTask; yield break; }
}

/// <summary>Placeholder: future MSIX/Store package enumeration.</summary>
public sealed class MsixStoreSource : ISource
{
    /// <summary>Source name.</summary>
    public string Name => nameof(MsixStoreSource);
    /// <inheritdoc />
    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    { await System.Threading.Tasks.Task.CompletedTask; yield break; }
}

/// <summary>Placeholder: future heuristic filesystem scanning of known install locations.</summary>
public sealed class HeuristicFsSource : ISource
{
    /// <summary>Source name.</summary>
    public string Name => nameof(HeuristicFsSource);
    /// <inheritdoc />
    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    { await System.Threading.Tasks.Task.CompletedTask; yield break; }
}
