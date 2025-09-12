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

    /// <summary>
    /// Enumerates Windows Uninstall registry keys to infer install directory and primary executable candidates.
    /// Matching strategy:
    ///  * Non-strict: case-insensitive substring on DisplayName OR key name.
    ///  * Strict: all query tokens must appear in DisplayName tokens (order-agnostic).
    /// Evidence keys: DisplayName, HasInstallLocation, HasDisplayIcon, WindowsInstaller, Key.
    /// </summary>
    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await System.Threading.Tasks.Task.Yield();
        if (string.IsNullOrWhiteSpace(query)) yield break;
        // Collect then yield to keep cancellation responsiveness predictable.
        var list = new List<AppHit>();
        if (!options.MachineOnly)
            EnumerateRoots(UninstallRootsUser, Scope.User, query, options, list, ct);
        if (!options.UserOnly)
            EnumerateRoots(UninstallRootsMachine, Scope.Machine, query, options, list, ct);
        foreach (var h in list) yield return h;
    }

    private void EnumerateRoots(IEnumerable<string> roots, Scope scope, string query, SourceOptions options, List<AppHit> sink, CancellationToken ct)
    {
        var queryTokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
                        if (string.IsNullOrEmpty(displayName)) continue; // skip entries with no friendly name

                        if (options.Strict)
                        {
                            if (!AllTokensPresent(dnLower!, queryTokens)) continue;
                        }
                        else
                        {
                            if (!dnLower!.Contains(query) && !keyLower.Contains(query)) continue;
                        }

                        var installLocation = (subKey.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                        if (string.IsNullOrEmpty(installLocation)) installLocation = null;
                        var displayIconRaw = (subKey.GetValue("DisplayIcon") as string)?.Trim();
                        var version = (subKey.GetValue("DisplayVersion") as string)?.Trim();
                        var windowsInstaller = (subKey.GetValue("WindowsInstaller") as int?) == 1 || (subKey.GetValue("WindowsInstaller") as string) == "1";

                        string? exeCandidate = ParseExeFromDisplayIcon(displayIconRaw);
                        // If installLocation missing but exeCandidate present, derive from it.
                        if (installLocation == null && exeCandidate != null)
                        {
                            try { installLocation = System.IO.Path.GetDirectoryName(exeCandidate); } catch { }
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
                    catch { /* swallow individual key errors */ }
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
            // Avoid obvious uninstall / temp directories.
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
        // Split off trailing ,resourceIndex (e.g., ",0")
        var commaIdx = s.IndexOf(',');
        if (commaIdx > 0) s = s.Substring(0, commaIdx);
        // Some entries quote with extra whitespace
        s = s.Trim();
        if (!s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        // Filter out typical uninstaller executables
        var fileLower = System.IO.Path.GetFileName(s).ToLowerInvariant();
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

    /// <summary>
    /// Enumerates Start Menu <c>.lnk</c> shortcut files (per-user and common) whose file name contains the <paramref name="query"/> substring
    /// and yields executable and install directory <see cref="AppHit"/> instances for resolved targets.
    /// </summary>
    /// <param name="query">Lowercase query substring (already normalized upstream).</param>
    /// <param name="options">Execution options controlling scope, strict mode, timeout and evidence emission.</param>
    /// <param name="ct">Cancellation token to abort enumeration early.</param>
    /// <remarks>
    /// Resolution uses a late–bound COM invocation via <c>WScript.Shell</c>. Errors resolving individual shortcuts are swallowed.
    /// Only <c>.exe</c> targets are considered; non-existent or non-executable targets are ignored.
    /// </remarks>
    /// <returns>Asynchronous sequence of raw (unscored) <see cref="AppHit"/> values.</returns>
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
    {
        await System.Threading.Tasks.Task.Yield();
        if (string.IsNullOrWhiteSpace(query)) yield break;
        var norm = query.ToLowerInvariant();
        System.Diagnostics.Process[] procs;
        try { procs = System.Diagnostics.Process.GetProcesses(); }
        catch { yield break; }

        foreach (var p in procs)
        {
            if (ct.IsCancellationRequested) yield break;
            string? name = null;
            string? mainModulePath = null;
            try { name = p.ProcessName; } catch { }
            try { mainModulePath = p.MainModule?.FileName; } catch { }
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(mainModulePath)) continue;
            var nameMatch = name != null && name.ToLowerInvariant().Contains(norm);
            var pathMatch = mainModulePath != null && mainModulePath.ToLowerInvariant().Contains(norm);
            if (!nameMatch && !pathMatch) continue;
            if (!string.IsNullOrEmpty(mainModulePath) && File.Exists(mainModulePath))
            {
                var scope = InferScope(mainModulePath);
                var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{"ProcessId", p.Id.ToString()},{"ProcessName", name ?? string.Empty}} : null;
                yield return new AppHit(HitType.Exe, scope, mainModulePath, null, PackageType.EXE, new[] { Name }, 0, evidence);
                var dir = Path.GetDirectoryName(mainModulePath);
                if (!string.IsNullOrEmpty(dir))
                    yield return new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.EXE, new[] { Name }, 0, evidence);
            }
        }
    }

    private static Scope InferScope(string path)
    {
        try
        {
            var lower = path.ToLowerInvariant();
            if (lower.Contains("\\users\\")) return Scope.User;
            return Scope.Machine;
        }
        catch { return Scope.Machine; }
    }
}

/// <summary>Placeholder: future PATH search using where.exe and directory scan.</summary>
public sealed class PathSearchSource : ISource
{
    /// <summary>Source name.</summary>
    public string Name => nameof(PathSearchSource);
    /// <inheritdoc />
    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await System.Threading.Tasks.Task.Yield();
        if (string.IsNullOrWhiteSpace(query)) yield break;
        var norm = query.ToLowerInvariant();

        // Strategy:
        // 1. Invoke where.exe for the raw query (best effort) – may fail silently.
        // 2. Enumerate PATH directories; for each *.exe whose file name contains the query substring emit hits.
        // 3. De-duplicate by full path (case-insensitive).

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Step 1: where.exe invocation (only if query looks like a single token without spaces)
        if (!query.Contains(' '))
        {
            List<AppHit>? buffered = null;
            try
            {
                using var p = new System.Diagnostics.Process();
                p.StartInfo.FileName = Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\where.exe");
                p.StartInfo.Arguments = ' ' + query;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.Start();
                var to = options.Timeout;
                var waitTask = Task.Run(() => p.WaitForExit((int)to.TotalMilliseconds), ct);
                var finished = await waitTask.ConfigureAwait(false);
                if (finished)
                {
                    while (!p.StandardOutput.EndOfStream)
                    {
                        if (ct.IsCancellationRequested) break;
                        var line = (await p.StandardOutput.ReadLineAsync().ConfigureAwait(false))?.Trim();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (!line.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!File.Exists(line)) continue;
                        if (!yielded.Add(line)) continue;
                        var scope = InferScope(line);
                        var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{"where","true"}} : null;
                        buffered ??= new List<AppHit>();
                        buffered.Add(new AppHit(HitType.Exe, scope, line, null, PackageType.EXE, new[] { Name }, 0, evidence));
                        var dir = Path.GetDirectoryName(line);
                        if (!string.IsNullOrEmpty(dir) && yielded.Add(dir + "::install"))
                            buffered.Add(new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.EXE, new[] { Name }, 0, evidence));
                    }
                }
                try { if (!p.HasExited) p.Kill(true); } catch { }
            }
            catch { }
            if (buffered != null)
            {
                foreach (var h in buffered)
                {
                    if (ct.IsCancellationRequested) yield break;
                    yield return h;
                }
            }
        }

        // Step 2: PATH directory scan
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (pathEnv.Length == 0) yield break;
        var parts = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in parts)
        {
            if (ct.IsCancellationRequested) yield break;
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files = Array.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
            }
            catch { }
            List<AppHit>? buffered = null;
            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) yield break;
                var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                if (!name.Contains(norm)) continue;
                if (!File.Exists(file)) continue;
                if (!yielded.Add(file)) continue;
                var scope = InferScope(file);
                var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{"PATH", dir}} : null;
                buffered ??= new List<AppHit>();
                buffered.Add(new AppHit(HitType.Exe, scope, file, null, PackageType.EXE, new[] { Name }, 0, evidence));
                var dirName = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dirName) && yielded.Add(dirName + "::install"))
                    buffered.Add(new AppHit(HitType.InstallDir, scope, dirName!, null, PackageType.EXE, new[] { Name }, 0, evidence));
            }
            if (buffered != null)
            {
                foreach (var h in buffered)
                {
                    if (ct.IsCancellationRequested) yield break;
                    yield return h;
                }
            }
        }
    }

    private static Scope InferScope(string path)
    {
        try
        {
            var lower = path.ToLowerInvariant();
            if (lower.Contains("\\users\\")) return Scope.User;
            return Scope.Machine;
        }
        catch { return Scope.Machine; }
    }
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

/// <summary>Enumerates Windows Services (ImagePath) and Scheduled Tasks (actions) to find executables referencing the query.</summary>
public sealed class ServicesTasksSource : ISource
{
    /// <summary>Source name.</summary>
    public string Name => nameof(ServicesTasksSource);

    /// <summary>Enumerate services and scheduled tasks for executables whose paths contain the query substring.</summary>
    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await System.Threading.Tasks.Task.Yield();
        if (string.IsNullOrWhiteSpace(query)) yield break;
        var norm = query.ToLowerInvariant();
        // Services: HKLM\System\CurrentControlSet\Services\*
        if (!options.UserOnly)
        {
            foreach (var hit in EnumerateServices(norm, options, ct))
            {
                if (ct.IsCancellationRequested) yield break;
                yield return hit;
            }
        }
        // Scheduled tasks: %SystemRoot%\System32\Tasks (XML files) – user & machine tasks; treat as machine scope if path under Windows dir, else user.
        foreach (var hit in EnumerateTasks(norm, options, ct))
        {
            if (ct.IsCancellationRequested) yield break;
            yield return hit;
        }
    }

    private IEnumerable<AppHit> EnumerateServices(string norm, SourceOptions options, CancellationToken ct)
    {
        Microsoft.Win32.RegistryKey? rk = null;
        try
        {
            rk = Registry.LocalMachine.OpenSubKey("System\\CurrentControlSet\\Services");
        }
        catch { }
        if (rk == null) yield break;
        foreach (var name in rk.GetSubKeyNames())
        {
            if (ct.IsCancellationRequested) yield break;
            Microsoft.Win32.RegistryKey? sk = null;
            try { sk = rk.OpenSubKey(name); } catch { }
            if (sk == null) continue;
            string? imagePath = null;
            try { imagePath = sk.GetValue("ImagePath") as string; } catch { }
            if (string.IsNullOrWhiteSpace(imagePath)) continue;
            // Expand environment variables and strip quotes
            imagePath = Environment.ExpandEnvironmentVariables(imagePath).Trim().Trim('"');
            // Some service commands contain arguments; split on first .exe or .bat or .cmd
            string exeCandidate = ExtractExecutablePath(imagePath);
            if (string.IsNullOrWhiteSpace(exeCandidate)) continue;
            string lowerExe = exeCandidate.ToLowerInvariant();
            if (!lowerExe.Contains(norm)) continue;
            if (!File.Exists(exeCandidate)) continue;
            var scope = ServicesScopeFromPath(exeCandidate);
            if (options.MachineOnly && scope == Scope.User) continue;
            if (options.UserOnly && scope == Scope.Machine) continue;
            var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{"Service", name}} : null;
            yield return new AppHit(HitType.Exe, scope, exeCandidate, null, PackageType.EXE, new[] { Name }, 0, evidence);
            var dir = Path.GetDirectoryName(exeCandidate);
            if (!string.IsNullOrEmpty(dir))
                yield return new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.EXE, new[] { Name }, 0, evidence);
        }
    }

    private IEnumerable<AppHit> EnumerateTasks(string norm, SourceOptions options, CancellationToken ct)
    {
        string tasksRoot = Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\Tasks");
        if (string.IsNullOrWhiteSpace(tasksRoot) || !Directory.Exists(tasksRoot)) yield break;
        IEnumerable<string> taskFiles = Array.Empty<string>();
        try { taskFiles = Directory.EnumerateFiles(tasksRoot, "*", SearchOption.AllDirectories); } catch { }
        foreach (var tf in taskFiles)
        {
            if (ct.IsCancellationRequested) yield break;
            // Quick substring scan to avoid loading XML unless likely match.
            string? content = null;
            try { content = File.ReadAllText(tf); } catch { }
            if (string.IsNullOrEmpty(content)) continue;
            if (!content.Contains(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (!content.Contains(norm, StringComparison.OrdinalIgnoreCase)) continue; // coarse filter
            // Very naive XML extraction: look for <Command>...</Command>
            foreach (var exe in ExtractCommands(content))
            {
                if (ct.IsCancellationRequested) yield break;
                var lower = exe.ToLowerInvariant();
                if (!lower.Contains(norm)) continue;
                if (!File.Exists(exe)) continue;
                var scope = ServicesScopeFromPath(exe);
                if (options.MachineOnly && scope == Scope.User) continue;
                if (options.UserOnly && scope == Scope.Machine) continue;
                var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{"TaskFile", tf}} : null;
                yield return new AppHit(HitType.Exe, scope, exe, null, PackageType.EXE, new[] { Name }, 0, evidence);
                var dir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(dir))
                    yield return new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.EXE, new[] { Name }, 0, evidence);
            }
        }
    }

    private static string ExtractExecutablePath(string imagePath)
    {
        try
        {
            // Common patterns: "C:\\Path\\App.exe" /service, C:\\Path\\App.exe -k something
            var idxExe = imagePath.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (idxExe == -1) return string.Empty;
            var startQuote = imagePath.LastIndexOf('"', idxExe);
            var start = startQuote >= 0 ? startQuote + 1 : 0;
            var candidate = imagePath.Substring(start, idxExe + 4 - start);
            return candidate.Trim('"');
        }
        catch { return string.Empty; }
    }

    private static IEnumerable<string> ExtractCommands(string xmlContent)
    {
        int pos = 0;
        while (true)
        {
            if (pos >= xmlContent.Length) yield break;
            var start = xmlContent.IndexOf("<Command>", pos, StringComparison.OrdinalIgnoreCase);
            if (start == -1) yield break;
            var end = xmlContent.IndexOf("</Command>", start, StringComparison.OrdinalIgnoreCase);
            if (end == -1) yield break;
            var innerStart = start + "<Command>".Length;
            var inner = xmlContent.Substring(innerStart, end - innerStart).Trim();
            pos = end + "</Command>".Length;
            if (string.IsNullOrEmpty(inner)) continue;
            // Commands may include arguments; extract initial executable path
            var exe = ExtractExecutablePath(inner);
            if (!string.IsNullOrEmpty(exe)) yield return exe;
        }
    }

    private static Scope ServicesScopeFromPath(string path)
    {
        try
        {
            var lower = path.ToLowerInvariant();
            if (lower.Contains("\\users\\")) return Scope.User;
            return Scope.Machine;
        }
        catch { return Scope.Machine; }
    }
}

/// <summary>Placeholder: future heuristic filesystem scanning of known install locations.</summary>
public sealed class HeuristicFsSource : ISource
{
    /// <summary>Source name.</summary>
    public string Name => nameof(HeuristicFsSource);
    /// <inheritdoc />
    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await System.Threading.Tasks.Task.Yield();
        if (string.IsNullOrWhiteSpace(query)) yield break;
        var norm = query.ToLowerInvariant();

        // Heuristic roots: user-scoped and machine-scoped
        var userRoots = new List<(string path, Scope scope)>()
        {
            (Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%\\Programs"), Scope.User),
            (Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%"), Scope.User),
            (Environment.ExpandEnvironmentVariables("%APPDATA%"), Scope.User)
        };
        var machineRoots = new List<(string path, Scope scope)>()
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Scope.Machine),
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Scope.Machine),
            (Environment.ExpandEnvironmentVariables("%PROGRAMDATA%"), Scope.Machine)
        };

        if (options.UserOnly) machineRoots.Clear();
        if (options.MachineOnly) userRoots.Clear();

        // Limit directory depth to avoid huge traversals. We'll cap at depth 3 from each root.
        const int MaxDepth = 3;
        var yieldedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var yieldedExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (root, scope) in userRoots.Concat(machineRoots))
        {
            if (ct.IsCancellationRequested) yield break;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            foreach (var hit in EnumerateRoot(root, scope, norm, options, MaxDepth, yieldedDirs, yieldedExe, ct))
            {
                if (ct.IsCancellationRequested) yield break;
                yield return hit;
            }
        }
    }

    private IEnumerable<AppHit> EnumerateRoot(string root, Scope scope, string norm, SourceOptions options, int maxDepth, HashSet<string> yieldedDirs, HashSet<string> yieldedExe, CancellationToken ct)
    {
        var stack = new Stack<(string path, int depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) yield break;
            var (current, depth) = stack.Pop();
            string? namePart = null;
            try { namePart = Path.GetFileName(current); } catch { }
            if (!string.IsNullOrEmpty(namePart) && namePart.ToLowerInvariant().Contains(norm))
            {
                // Directory name match → InstallDir heuristic
                if (yieldedDirs.Add(current))
                {
                    var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{"DirMatch", namePart}} : null;
                    yield return new AppHit(HitType.InstallDir, scope, current, null, PackageType.Unknown, new[] { Name }, 0, evidence);
                }
            }

            // Enumerate executables in this directory (top-level only) if within depth
            if (depth <= maxDepth)
            {
                IEnumerable<string> exes = Array.Empty<string>();
                try { exes = Directory.EnumerateFiles(current, "*.exe", SearchOption.TopDirectoryOnly); } catch { }
                foreach (var exe in exes)
                {
                    if (ct.IsCancellationRequested) yield break;
                    string? fileName = null;
                    try { fileName = Path.GetFileNameWithoutExtension(exe); } catch { }
                    if (string.IsNullOrEmpty(fileName)) continue;
                    if (!fileName.ToLowerInvariant().Contains(norm)) continue;
                    if (!File.Exists(exe)) continue;
                    if (yieldedExe.Add(exe))
                    {
                        var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{"ExeName", fileName}} : null;
                        yield return new AppHit(HitType.Exe, scope, exe, null, PackageType.Unknown, new[] { Name }, 0, evidence);
                        var dir = Path.GetDirectoryName(exe);
                        if (!string.IsNullOrEmpty(dir) && yieldedDirs.Add(dir))
                        {
                            var dirEvidence = options.IncludeEvidence ? new Dictionary<string,string>{{"FromExeDir", fileName}} : null;
                            yield return new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.Unknown, new[] { Name }, 0, dirEvidence);
                        }
                    }
                }
            }

            // Recurse into subdirectories if depth < maxDepth
            if (depth < maxDepth)
            {
                IEnumerable<string> subs = Array.Empty<string>();
                try { subs = Directory.EnumerateDirectories(current); } catch { }
                foreach (var sub in subs)
                {
                    if (ct.IsCancellationRequested) yield break;
                    stack.Push((sub, depth + 1));
                }
            }
        }
    }
}
