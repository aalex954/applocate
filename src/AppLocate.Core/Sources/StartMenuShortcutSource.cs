using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources;

/// <summary>Enumerates Start Menu .lnk shortcuts to infer executables and install directories.</summary>
public sealed class StartMenuShortcutSource : ISource
{
    public string Name => nameof(StartMenuShortcutSource);

    private static string[] GetUserRoots() => new[]
    {
        Environment.ExpandEnvironmentVariables("%AppData%\\Microsoft\\Windows\\Start Menu\\Programs")
    };
    private static string[] GetCommonRoots() => new[]
    {
        Environment.ExpandEnvironmentVariables("%ProgramData%\\Microsoft\\Windows\\Start Menu\\Programs").Replace("\n", string.Empty)
    };

    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await System.Threading.Tasks.Task.Yield();
        if (string.IsNullOrWhiteSpace(query)) yield break;
        var norm = query.ToLowerInvariant();
        var tokens = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!options.MachineOnly)
        {
            foreach (var r in Enumerate(GetUserRoots(), Scope.User, norm, tokens, options, dedup, ct)) yield return r;
        }
        if (!options.UserOnly)
        {
            foreach (var r in Enumerate(GetCommonRoots(), Scope.Machine, norm, tokens, options, dedup, ct)) yield return r;
        }
    }

    private IEnumerable<AppHit> Enumerate(IEnumerable<string> roots, Scope scope, string norm, string[] tokens, SourceOptions options, HashSet<string> dedup, CancellationToken ct)
    {
        foreach (var root in roots)
        {
            if (ct.IsCancellationRequested) yield break;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            IEnumerable<string> files = Array.Empty<string>();
            try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); } catch { }
            foreach (var lnk in files)
            {
                if (ct.IsCancellationRequested) yield break;
                var fileName = Path.GetFileNameWithoutExtension(lnk).ToLowerInvariant();
                if (!Matches(fileName, norm, tokens, options.Strict)) continue;
                string? target = null;
                try { target = ResolveShortcut(lnk); } catch { }
                if (string.IsNullOrWhiteSpace(target)) continue;
                target = Environment.ExpandEnvironmentVariables(target).Trim().Trim('"');
                if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(target)) continue;
                if (!dedup.Add(target)) continue;
                var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{"Shortcut", lnk}} : null;
                yield return new AppHit(HitType.Exe, scope, target, null, PackageType.EXE, new[] { Name }, 0, evidence);
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && dedup.Add(dir + "::install"))
                    yield return new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.EXE, new[] { Name }, 0, evidence);
            }
        }
    }

    private static bool Matches(string fileNameLower, string norm, string[] tokens, bool strict)
    {
        if (!strict)
        {
            if (fileNameLower.Contains(norm)) return true;
            if (tokens.Length > 1)
            {
                var collapsed = string.Concat(tokens);
                if (fileNameLower.Contains(collapsed)) return true;
                bool all = true;
                foreach (var t in tokens) { if (!fileNameLower.Contains(t)) { all = false; break; } }
                if (all) return true;
            }
            return false;
        }
        var parts = fileNameLower.Split(new[]{'.','-','_',' '}, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var t in tokens)
        {
            bool found = false;
            foreach (var p in parts) { if (p.Contains(t)) { found = true; break; } }
            if (!found) return false;
        }
        return true;
    }

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
