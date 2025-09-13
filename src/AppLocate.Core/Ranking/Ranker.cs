using System;
using AppLocate.Core.Models;

namespace AppLocate.Core.Ranking;

/// <summary>
/// Heuristic ranking engine combining token coverage, evidence signals, multi-source strength, and type weighting.
/// Scores are clamped to [0,1]. Intent: monotonic improvements from stronger evidence; diminishing returns on source count.
/// </summary>
/// <summary>
/// Public ranking helper exposing scoring for <see cref="AppLocate.Core.Models.AppHit"/> instances.
/// Made public to allow CLI and future plugins to reuse consistent scoring semantics.
/// </summary>
public static class Ranker
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
    /// <summary>
    /// Scores an <see cref="AppHit"/> against a normalized (lowercase) query.
    /// Refinement notes (Sep 2025):
    ///  - Distinguish inherent alias equivalence (query alias -> filename) from evidence supplied alias (AliasMatched evidence key).
    ///  - Introduce token span tightness boost (contiguous coverage of all tokens in filename) up to +0.04.
    ///  - Replace ln-based multi-source diminishing returns with harmonic accumulator (H_n) scaled to cap +0.18 for smoother early gains and softer tail.
    ///  - Add lightweight fuzzy ratio scaling using normalized Levenshtein distance on filename vs query (capped influence +0.06, only when no exact match).
    ///  - Additional path quality penalties for known ephemeral / cache roots (AppData Temp, Installer, Edge update stubs) for ranking stability.
    /// </summary>
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
                    score += jaccard * 0.08; // slightly reduced to let span boost differentiate
            }
        }

    // 2. Exact filename match (strong) – additive but within reasonable cap. Alias equivalence considered.
        if (!string.IsNullOrEmpty(fileName))
        {
            if (fileName.Equals(query, StringComparison.OrdinalIgnoreCase)) score += 0.30;
            else if (AliasEquivalent(query, fileName, out var alias))
            {
        score += 0.22; // slight reduction to differentiate from explicit evidence-based alias matches
            }
            else if (tokenCoverage == 0 && fileName.Contains(query)) score += 0.12; // legacy partial boost if tokens missed
        }

        // 2b. For Config/Data hits, allow directory-name alias equivalence to contribute (common pattern: query 'vscode' directory 'Code')
        if ((hit.Type == HitType.Config || hit.Type == HitType.Data) && !string.IsNullOrEmpty(dirName))
        {
            if (dirName.Equals(query, StringComparison.OrdinalIgnoreCase)) score += 0.20;
            else if (AliasEquivalent(query, dirName!, out var dirAlias)) score += 0.18; // moderate boost; less than exe alias boost
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
            if (ev.ContainsKey("AliasMatched")) score += 0.14; // evidence-driven alias stronger than inferred equivalence
            if (ev.ContainsKey("BrokenShortcut")) score -= 0.15; // penalty
        }

        // 4. Path quality penalties (extend to installer caches & ephemeral roots)
        if (lowerPath.Contains("\\temp\\") || lowerPath.Contains("/temp/") || lowerPath.Contains("%temp%")) score -= 0.10; // ephemeral (handles mixed separators)
        if (lowerPath.Contains("\\installer\\") || lowerPath.EndsWith(".tmp.exe", StringComparison.OrdinalIgnoreCase)) score -= 0.08;
        if (lowerPath.Contains("edgeupdate\\temp")) score -= 0.05; // updater staging area
        if (lowerPath.Contains("appdata\\local\\temp")) score -= 0.05;

        // 4b. Token span tightness: are all tokens covered contiguously in filename?
        if (tokensQ.Count > 1 && !string.IsNullOrEmpty(fileName))
        {
            // Evaluate contiguous token span inside filename (ignoring separators '-', '_')
            var simplified = fileName.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
            var joinedInOrder = string.Join(string.Empty, tokensQ); // e.g., "googlechrome"
            if (simplified.Contains(joinedInOrder, StringComparison.OrdinalIgnoreCase))
            {
                // Ensure we aren't trivially matching because tokens collapsed due to inserted unrelated tokens: require each token present separately too
                bool allPresent = true;
                foreach (var t in tokensQ) if (!simplified.Contains(t, StringComparison.OrdinalIgnoreCase)) { allPresent = false; break; }
                if (allPresent)
                    score += 0.08; // increased tight span boost to outrank spaced noisy variants
            }
        }

        // 5. Multi-source diminishing returns (harmonic series scaling) cap +0.18
        var sourceCount = hit.Source?.Length ?? 0;
        if (sourceCount > 1)
        {
            double harmonic = 0; // H_n - 1 (exclude the first source's baseline)
            for (int i = 2; i <= sourceCount; i++) harmonic += 1.0 / i; // starts with 1/2
            // Normalize relative to an expected practical max, e.g., 6 sources -> H_6 -1 ~= 1.45 -1 = 0.45; scale to [0,1]
            double norm = harmonic / 0.9; // allow >6 sources to saturate towards 1
            if (norm > 1) norm = 1;
            score += norm * 0.18;
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

        // 7. Fuzzy filename vs query Levenshtein (only when not exact / alias exact). Lightweight manual impl bounded length.
        if (!string.IsNullOrEmpty(fileName) && !fileName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            var fuzzy = FuzzyRatio(fileName, query); // 0..1 similarity
            if (fuzzy > 0.5 && tokenCoverage < 1) // only contribute when not perfect token coverage
            {
                score += (fuzzy - 0.5) * 0.12; // maps 0.5..1 -> 0..0.06
            }
        }

        // 8. Post adjustments: mild reward for deeper token coverage precision (all tokens matched and exact file)
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

    // Lightweight Levenshtein similarity ratio (1 - distance/maxLen)
    private static double FuzzyRatio(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        // Cap to avoid excessive cost on very long paths (only filenames here so fine)
        var la = a.Length; var lb = b.Length;
        var d = new int[la + 1, lb + 1];
        for (int i = 0; i <= la; i++) d[i,0] = i;
        for (int j = 0; j <= lb; j++) d[0,j] = j;
        for (int i = 1; i <= la; i++)
        {
            for (int j = 1; j <= lb; j++)
            {
                int cost = a[i-1] == b[j-1] ? 0 : 1;
                int del = d[i-1,j] + 1;
                int ins = d[i,j-1] + 1;
                int sub = d[i-1,j-1] + cost;
                int min = del < ins ? del : ins;
                if (sub < min) min = sub;
                d[i,j] = min;
            }
        }
        var dist = d[la,lb];
        double maxLen = Math.Max(la, lb);
        var ratio = 1.0 - (dist / maxLen);
        if (ratio < 0) ratio = 0; if (ratio > 1) ratio = 1;
        return ratio;
    }
}
