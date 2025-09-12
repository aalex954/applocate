using System;
using AppLocate.Core.Models;

namespace AppLocate.Core.Ranking;

/// <summary>
/// Heuristic ranking engine combining token coverage, evidence signals, multi-source strength, and type weighting.
/// Scores are clamped to [0,1]. Intent: monotonic improvements from stronger evidence; diminishing returns on source count.
/// </summary>
internal static class Ranker
{
    // Simple built-in alias dictionary (will later be externalized via plugin/rule pack)
    private static readonly System.Collections.Generic.Dictionary<string,string[]> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "vscode", new[]{"code","visual studio code"} },
        { "code", new[]{"vscode","visual studio code"} },
        { "chrome", new[]{"google chrome"} },
        { "google chrome", new[]{"chrome"} },
        { "edge", new[]{"microsoft edge"} },
        { "notepad++", new[]{"notepadpp","npp"} },
        { "powershell", new[]{"pwsh"} },
        { "pwsh", new[]{"powershell"} }
    };

    private static bool AliasEquivalent(string query, string candidateFileName, out string? aliasMatched)
    {
        aliasMatched = null;
        foreach (var kv in _aliases)
        {
            if (string.Equals(kv.Key, query, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var a in kv.Value)
                {
                    if (string.Equals(a, candidateFileName, StringComparison.OrdinalIgnoreCase)) { aliasMatched = a; return true; }
                }
            }
            // reverse direction
            if (string.Equals(kv.Key, candidateFileName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var a in kv.Value)
                {
                    if (string.Equals(a, query, StringComparison.OrdinalIgnoreCase)) { aliasMatched = a; return true; }
                }
            }
        }
        return false;
    }
    /// <summary>Scores an <see cref="AppHit"/> against a normalized (lowercase) query.</summary>
    public static double Score(string normalizedQuery, AppHit hit)
    {
        if (string.IsNullOrEmpty(normalizedQuery)) return 0;
        var path = hit.Path ?? string.Empty;
        var lowerPath = path.ToLowerInvariant();
        var query = normalizedQuery.ToLowerInvariant();
        double score = 0;

        // 1. Token set similarity (Jaccard) over filename & parent directory names
        var fileName = Safe(() => System.IO.Path.GetFileNameWithoutExtension(path)?.ToLowerInvariant());
        var dirName = Safe(() => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? string.Empty)?.ToLowerInvariant());
        var tokensQ = Tokenize(query);
        var tokensCand = Tokenize(string.Join(' ', new[]{fileName, dirName}));
        double tokenCoverage = 0;
        if (tokensQ.Count > 0 && tokensCand.Count > 0)
        {
            int match = 0;
            foreach (var t in tokensQ) if (tokensCand.Contains(t)) match++;
            tokenCoverage = (double)match / tokensQ.Count; // 0..1
            score += tokenCoverage * 0.25; // up to +0.25 replacing older substring/partial boosts
        }
        else if (lowerPath.Contains(query))
        {
            score += 0.15; // fallback simple substring when tokenization gives nothing
        }

        // 1c. Collapsed substring fuzzy: if no token coverage and no direct substring (with spaces), try space-stripped comparison
        if (tokenCoverage == 0)
        {
            var collapsedQuery = query.Replace(" ", string.Empty);
            var collapsedName = (fileName ?? string.Empty).Replace(" ", string.Empty);
            if (!string.IsNullOrEmpty(collapsedQuery) && collapsedName.Contains(collapsedQuery) && !fileName!.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.08; // moderate fuzzy boost
            }
        }

        // 1b. Fuzzy token ratio (very lightweight): token overlap over union (if partial mismatch) – adds up to +0.10
        if (tokensQ.Count > 0 && tokensCand.Count > 0)
        {
            var union = new System.Collections.Generic.HashSet<string>(tokensCand, StringComparer.OrdinalIgnoreCase);
            foreach (var t in tokensQ) union.Add(t);
            int inter = 0; foreach (var t in tokensQ) if (tokensCand.Contains(t)) inter++;
            if (union.Count > 0)
            {
                var jaccard = (double)inter / union.Count; // 0..1
                if (jaccard > 0 && jaccard < 1) // only partial matches
                    score += jaccard * 0.10;
            }
        }

        // 2. Exact filename match (strong) – additive but within reasonable cap. Alias equivalence considered.
        if (!string.IsNullOrEmpty(fileName))
        {
            if (fileName.Equals(query, StringComparison.OrdinalIgnoreCase)) score += 0.30;
            else if (AliasEquivalent(query, fileName, out var alias))
            {
                score += 0.20; // alias match slightly less than direct exact
            }
            else if (tokenCoverage == 0 && fileName.Contains(query)) score += 0.12; // legacy partial boost if tokens missed
        }

        // 3. Evidence-based boosts & synergies
        var ev = hit.Evidence;
        if (ev != null)
        {
            // Map encoded alias evidence to a consistent boost (may have been added by source layer)
            bool shortcut = ev.ContainsKey("Shortcut");
            bool process = ev.ContainsKey("ProcessId");
            if (shortcut) score += 0.10;
            if (process) score += 0.08;
            if (shortcut && process) score += 0.05; // synergy: user launched + Start Menu presence
            if (ev.ContainsKey("where")) score += 0.05;
            if (ev.ContainsKey("DirMatch")) score += 0.06;
            if (ev.ContainsKey("ExeName")) score += 0.04;
            if (ev.ContainsKey("AliasMatched")) score += 0.12; // planned alias dictionary
            if (ev.ContainsKey("BrokenShortcut")) score -= 0.15; // penalty
        }

    // 4. Path quality penalties (extend to installer caches)
    if (lowerPath.Contains("\\temp\\") || lowerPath.Contains("/temp/") || lowerPath.Contains("%temp%")) score -= 0.10; // ephemeral (handles mixed separators)
    if (lowerPath.Contains("\\installer\\") || lowerPath.EndsWith(".tmp.exe", StringComparison.OrdinalIgnoreCase)) score -= 0.08;

        // 5. Multi-source diminishing returns (ln scaling, cap +0.18)
        var sourceCount = hit.Source?.Length ?? 0;
        if (sourceCount > 1)
        {
            var multiBoost = Math.Log(sourceCount, Math.E) / Math.Log(6, Math.E); // normalized ~0..1 for ~1..6 sources
            if (multiBoost < 0) multiBoost = 0;
            if (multiBoost > 1) multiBoost = 1;
            score += multiBoost * 0.18;
        }

        // 6. Type baseline weighting
        score += hit.Type switch
        {
            HitType.Exe => 0.08,
            HitType.Config => 0.05,
            HitType.InstallDir => 0.04,
            HitType.Data => 0.03,
            _ => 0
        };

        // 7. Post adjustments: mild reward for deeper token coverage precision (all tokens matched and exact file)
        if (tokenCoverage == 1 && !string.IsNullOrEmpty(fileName) && fileName.Equals(query, StringComparison.OrdinalIgnoreCase))
            score += 0.05;

        // Clamp
        if (score > 1.0) score = 1.0;
        if (score < 0) score = 0;
        return score;
    }

    private static string? Safe(Func<string?> f) { try { return f(); } catch { return null; } }

    private static System.Collections.Generic.HashSet<string> Tokenize(string? value)
    {
        var set = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value)) return set;
        var parts = value.Split(new[]{' ','-','_','.'}, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.Length == 0) continue;
            set.Add(t);
        }
        return set;
    }
}
