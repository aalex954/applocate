using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Sources;

/// <summary>Searches PATH and invokes where.exe to locate executables matching query variants.</summary>
public sealed class PathSearchSource : ISource
{
    public string Name => nameof(PathSearchSource);

    public async IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await System.Threading.Tasks.Task.Yield();
        if (string.IsNullOrWhiteSpace(query)) yield break;
        var norm = query.ToLowerInvariant();
        var pathTokens = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var aliasForms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { norm };
        if (norm.Contains(' ')) aliasForms.Add(norm.Replace(" ", string.Empty));
        if (norm.Contains('-')) aliasForms.Add(norm.Replace("-", string.Empty));
        if (norm.Contains(' ') && !aliasForms.Contains(norm.Replace(' ', '-'))) aliasForms.Add(norm.Replace(' ', '-'));
        if (norm.Contains('-') && !aliasForms.Contains(norm.Replace('-', ' '))) aliasForms.Add(norm.Replace('-', ' '));

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // where.exe variants
        var whereCandidates = new List<string>();
        if (!query.Contains(' ')) whereCandidates.Add(query);
        else
        {
            var collapsed = query.Replace(" ", string.Empty);
            if (collapsed.Length > 2) whereCandidates.Add(collapsed);
            if (query.Contains(' ') && !query.Contains('-'))
            {
                var hyphen = query.Replace(' ', '-');
                if (hyphen.Length > 2) whereCandidates.Add(hyphen);
            }
        }
        foreach (var wc in whereCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            List<AppHit>? buffered = null;
            try
            {
                using var p = new System.Diagnostics.Process();
                p.StartInfo.FileName = Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\where.exe");
                p.StartInfo.Arguments = ' ' + wc;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.Start();
                var to = options.Timeout;
                var waitTask = System.Threading.Tasks.Task.Run(() => p.WaitForExit((int)to.TotalMilliseconds), ct);
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
                        var evidence = options.IncludeEvidence ? new Dictionary<string,string>{{"where","true"},{"WhereQuery", wc}} : null;
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

        // PATH scan
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (pathEnv.Length == 0) yield break;
        var parts = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in parts)
        {
            if (ct.IsCancellationRequested) yield break;
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files = Array.Empty<string>();
            try { files = Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly); } catch { }
            List<AppHit>? buffered = null;
            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) yield break;
                var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                bool match;
                if (!options.Strict)
                {
                    string Collapse(string s)
                    {
                        Span<char> buffer = stackalloc char[s.Length];
                        int idx = 0;
                        foreach (var ch in s)
                        {
                            if (ch == ' ' || ch == '-' || ch == '_' || ch == '.') continue;
                            buffer[idx++] = ch;
                        }
                        return new string(buffer.Slice(0, idx));
                    }
                    var collapsedName = Collapse(name);
                    var plainMatch = aliasForms.Any(a => name.Contains(a, StringComparison.OrdinalIgnoreCase) || name.Equals(a, StringComparison.OrdinalIgnoreCase));
                    var collapsedMatch = !plainMatch && aliasForms.Any(a =>
                    {
                        var ca = Collapse(a);
                        if (ca.Length == 0) return false;
                        return collapsedName.Contains(ca, StringComparison.OrdinalIgnoreCase);
                    });
                    match = plainMatch || collapsedMatch;
                }
                else
                {
                    match = pathTokens.All(t => name.Contains(t));
                }
                if (!match) continue;
                if (!File.Exists(file)) continue;
                if (!yielded.Add(file)) continue;
                var scope = InferScope(file);
                Dictionary<string,string>? evidence = null;
                if (options.IncludeEvidence)
                {
                    evidence = new Dictionary<string,string>{{"PATH", dir},{"ExeName", Path.GetFileName(file)}};
                }
                buffered ??= new List<AppHit>();
                buffered.Add(new AppHit(HitType.Exe, scope, file, null, PackageType.EXE, new[] { Name }, 0, evidence));
                var dirName = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dirName) && yielded.Add(dirName + "::install"))
                {
                    Dictionary<string,string>? dirEvidence = evidence;
                    if (options.IncludeEvidence && dirEvidence != null && !dirEvidence.ContainsKey("DirMatch"))
                        dirEvidence = new Dictionary<string,string>(dirEvidence) { {"DirMatch","true"} };
                    buffered.Add(new AppHit(HitType.InstallDir, scope, dirName!, null, PackageType.EXE, new[] { Name }, 0, dirEvidence));
                }
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

        // Variant probe into Program Files
        if (!ct.IsCancellationRequested && (pathTokens.Length > 1 || norm.Contains('-')))
        {
            string? pf86 = null; string? pf = null;
            try { pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86); } catch { }
            try { pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); } catch { }
            var roots = new List<string>();
            if (!string.IsNullOrEmpty(pf86)) roots.Add(pf86);
            if (!string.IsNullOrEmpty(pf) && pf != pf86) roots.Add(pf!);
            foreach (var variant in aliasForms)
            {
                if (ct.IsCancellationRequested) break;
                var collapsed = variant.Replace(" ", string.Empty).Replace("-", string.Empty);
                if (collapsed.Length < 3) continue;
                foreach (var root in roots)
                {
                    if (ct.IsCancellationRequested) break;
                    var candidates = new []
                    {
                        Path.Combine(root, variant, "bin", variant + ".exe"),
                        Path.Combine(root, variant, variant + ".exe"),
                        Path.Combine(root, collapsed, "bin", collapsed + ".exe"),
                        Path.Combine(root, collapsed, collapsed + ".exe"),
                    };
                    foreach (var cand in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (ct.IsCancellationRequested) break;
                        if (!cand.EndsWith('.' + "exe", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!File.Exists(cand)) continue;
                        if (!yielded.Add(cand)) continue;
                        var scope = InferScope(cand);
                        Dictionary<string,string>? evidence = null;
                        if (options.IncludeEvidence)
                            evidence = new Dictionary<string,string>{{"VariantProbe", variant},{"Root", root}};
                        yield return new AppHit(HitType.Exe, scope, cand, null, PackageType.EXE, new[]{ Name }, 0, evidence);
                        var dirOut = Path.GetDirectoryName(cand);
                        if (!string.IsNullOrEmpty(dirOut) && yielded.Add(dirOut + "::install"))
                            yield return new AppHit(HitType.InstallDir, scope, dirOut!, null, PackageType.EXE, new[]{ Name }, 0, evidence);
                    }
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
