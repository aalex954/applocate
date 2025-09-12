using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AppLocate.Core.Rules;

/// <summary>Placeholder rules engine. Will parse YAML and produce config/data paths.</summary>
internal sealed class RulesEngine
{
    /// <summary>
    /// Loads a very small subset of YAML supporting a list of items each with:
    /// - match.anyOf: ["Name", "Alt"]
    /// - config: ["%APPDATA%/..."]
    /// - data: ["%LOCALAPPDATA%/..." ]
    /// This avoids a heavy YAML dependency; upgrade to YamlDotNet later if schema grows.
    /// </summary>
    public Task<IReadOnlyList<ResolvedRule>> LoadAsync(string file, CancellationToken ct)
    {
        if (!File.Exists(file)) return Task.FromResult<IReadOnlyList<ResolvedRule>>(Array.Empty<ResolvedRule>());
        var lines = File.ReadAllLines(file);
        var rules = new List<ResolvedRule>();
        List<string>? currentMatch = null;
        List<string>? currentConfig = null;
        List<string>? currentData = null;
        static List<string> ParseInlineArray(string line)
        {
            // expects ["a", "b"] or ["a"]
            var start = line.IndexOf('[');
            var end = line.IndexOf(']');
            if (start < 0 || end < start) return new List<string>();
            var inner = line.Substring(start + 1, end - start - 1);
            return inner.Split(',').Select(s => s.Trim().Trim('"')).Where(s => s.Length > 0).ToList();
        }
        void Flush()
        {
            if (currentMatch != null)
            {
                rules.Add(new ResolvedRule(currentMatch.ToArray(), (currentConfig ?? new()).ToArray(), (currentData ?? new()).ToArray()));
                currentMatch = null; currentConfig = null; currentData = null;
            }
        }
        foreach (var raw in lines)
        {
            if (ct.IsCancellationRequested) break;
            var line = raw.Trim();
            if (line.StartsWith("#") || line.Length == 0) continue;
            if (line.StartsWith("- match")) { Flush(); continue; }
            if (line.StartsWith("anyOf:"))
            {
                currentMatch = ParseInlineArray(line);
                continue;
            }
            if (line.StartsWith("config:")) { currentConfig = ParseInlineArray(line); continue; }
            if (line.StartsWith("data:")) { currentData = ParseInlineArray(line); continue; }
        }
        Flush();
        return Task.FromResult<IReadOnlyList<ResolvedRule>>(rules);
    }
}

internal sealed record ResolvedRule(string[] MatchAnyOf, string[] Config, string[] Data);
