using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources;

/// <summary>Heuristic filesystem scan of known install locations (shallow, bounded).</summary>
public sealed class HeuristicFsSource : ISource
{
    public string Name => nameof(HeuristicFsSource);

    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await System.Threading.Tasks.Task.Yield();
        if (string.IsNullOrWhiteSpace(query)) yield break;
        var norm = query.ToLowerInvariant();
        var tokens = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

        const int MaxDepth = 3;
        var stopTime = DateTime.UtcNow + (options.Timeout == TimeSpan.Zero ? TimeSpan.FromSeconds(2) : options.Timeout);
        var yieldedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var yieldedExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (root, scope) in userRoots.Concat(machineRoots))
        {
            if (ct.IsCancellationRequested) yield break;
            if (DateTime.UtcNow > stopTime) yield break;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            foreach (var hit in EnumerateRoot(root, scope, norm, tokens, options, MaxDepth, yieldedDirs, yieldedExe, stopTime, ct))
            {
                if (ct.IsCancellationRequested) yield break;
                yield return hit;
            }
        }
    }

    private IEnumerable<AppHit> EnumerateRoot(string root, Scope scope, string norm, string[] tokens, SourceOptions options, int maxDepth, HashSet<string> yieldedDirs, HashSet<string> yieldedExe, DateTime stopTime, CancellationToken ct)
    {
        var stack = new Stack<(string path, int depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) yield break;
            if (DateTime.UtcNow > stopTime) yield break;
            var (current, depth) = stack.Pop();
            string? namePart = null;
            try { namePart = Path.GetFileName(current); } catch { }
            if (!string.IsNullOrEmpty(namePart))
            {
                var lowerDir = namePart.ToLowerInvariant();
                bool dirMatch = options.Strict ? tokens.All(t => lowerDir.Contains(t)) : lowerDir.Contains(norm);
                if (dirMatch && yieldedDirs.Add(current))
                {
                    var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{EvidenceKeys.DirMatch, namePart}} : null;
                    yield return new AppHit(HitType.InstallDir, scope, current, null, PackageType.Unknown, new[] { Name }, 0, evidence);
                }
            }

            if (depth <= maxDepth)
            {
                IEnumerable<string> exes = Array.Empty<string>();
                try { exes = Directory.EnumerateFiles(current, "*.exe", SearchOption.TopDirectoryOnly); } catch { }
                foreach (var exe in exes)
                {
                    if (ct.IsCancellationRequested) yield break;
                    if (DateTime.UtcNow > stopTime) yield break;
                    string? fileName = null;
                    try { fileName = Path.GetFileNameWithoutExtension(exe); } catch { }
                    if (string.IsNullOrEmpty(fileName)) continue;
                    var lowerFile = fileName.ToLowerInvariant();
                    bool exeMatch = options.Strict ? tokens.All(t => lowerFile.Contains(t)) : lowerFile.Contains(norm);
                    if (!exeMatch) continue;
                    if (!File.Exists(exe)) continue;
                    if (yieldedExe.Add(exe))
                    {
                        var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{EvidenceKeys.ExeName, fileName}} : null;
                        yield return new AppHit(HitType.Exe, scope, exe, null, PackageType.Unknown, new[] { Name }, 0, evidence);
                        var dir = Path.GetDirectoryName(exe);
                        if (!string.IsNullOrEmpty(dir) && yieldedDirs.Add(dir))
                        {
                            var dirEvidence = options.IncludeEvidence ? new Dictionary<string,string>{{EvidenceKeys.FromExeDir, fileName}} : null;
                            yield return new AppHit(HitType.InstallDir, scope, dir!, null, PackageType.Unknown, new[] { Name }, 0, dirEvidence);
                        }
                    }
                }
            }

            if (depth < maxDepth)
            {
                IEnumerable<string> subs = Array.Empty<string>();
                try { subs = Directory.EnumerateDirectories(current); } catch { }
                foreach (var sub in subs)
                {
                    if (ct.IsCancellationRequested) yield break;
                    if (DateTime.UtcNow > stopTime) yield break;
                    var leaf = Path.GetFileName(sub)?.ToLowerInvariant();
                    if (leaf == "node_modules" || leaf == ".git" || leaf == "temp" || leaf == "tmp") continue;
                    stack.Push((sub, depth + 1));
                }
            }
        }
    }
}
