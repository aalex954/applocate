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
    private static readonly System.Collections.Generic.Dictionary<string, string[]> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "vscode", new[]{"code","visual studio code"} },
        { "code", new[]{"vscode","visual studio code"} },
        { "chrome", new[]{"google chrome"} },
        { "google chrome", new[]{"chrome"} },
        { "edge", new[]{"microsoft edge"} },
        { "notepad++", new[]{"notepadpp","npp"} },
        { "powershell", new[]{"pwsh"} },
    { "pwsh", new[]{"powershell"} },
    // oh-my-posh / winget id variants
    { "ohmyposh", new[]{"oh my posh","jandedobbeleer.ohmyposh","oh-my-posh"} },
    { "oh-my-posh", new[]{"oh my posh","ohmyposh","jandedobbeleer.ohmyposh"} },
    { "oh my posh", new[]{"ohmyposh","oh-my-posh","jandedobbeleer.ohmyposh"} },
    { "jandedobbeleer.ohmyposh", new[]{"oh my posh","ohmyposh","oh-my-posh"} }
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
    public static double Score(string normalizedQuery, AppHit hit) => ScoreInternal(normalizedQuery, hit, null);
    /// <summary>
    /// Computes the score for an <see cref="AppHit"/> while also returning a structured <see cref="ScoreBreakdown"/> of component contributions.
    /// </summary>
    /// <param name="normalizedQuery">Lowercase, token normalized query.</param>
    /// <param name="hit">Hit to evaluate.</param>
    /// <returns>Tuple containing the final score (0..1) and a populated <see cref="ScoreBreakdown"/>.</returns>
    public static (double score, ScoreBreakdown breakdown) ScoreWithBreakdown(string normalizedQuery, AppHit hit)
    {
        var signals = new System.Collections.Generic.Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var s = ScoreInternal(normalizedQuery, hit, signals);
        var breakdown = BuildBreakdown(signals, s);
        return (s, breakdown);
    }

    private static double ScoreInternal(string normalizedQuery, AppHit hit, System.Collections.Generic.Dictionary<string, double>? signals)
    {
        if (string.IsNullOrEmpty(normalizedQuery)) return 0;
        var path = hit.Path ?? string.Empty;
        var lowerPath = path.ToLowerInvariant();
        var query = normalizedQuery.ToLowerInvariant();
        double score = 0;
        string collapsedQuery = new string(query.Where(ch => !(ch == ' ' || ch == '-' || ch == '.')).ToArray());
        if (collapsedQuery.Length < 2) collapsedQuery = query;

        // 1. Token set similarity (Jaccard) over filename & parent directory names
        var fileName = Safe(() => System.IO.Path.GetFileNameWithoutExtension(path)?.ToLowerInvariant());
        var dirName = Safe(() => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? string.Empty)?.ToLowerInvariant());
        var tokensQ = Tokenize(query);
        var tokensCand = Tokenize(string.Join(' ', new[] { fileName, dirName }));
        // Secondary punctuation/camel splits: expand token sets without overwriting originals
        if (tokensQ.Count > 0)
        {
            foreach (var t in tokensQ.ToArray())
            {
                foreach (var exp in ExpandToken(t)) tokensQ.Add(exp);
            }
        }
        if (tokensCand.Count > 0)
        {
            foreach (var t in tokensCand.ToArray())
            {
                foreach (var exp in ExpandToken(t)) tokensCand.Add(exp);
            }
        }
        double tokenCoverage = 0;
        if (tokensQ.Count > 0 && tokensCand.Count > 0)
        {
            int match = 0;
            foreach (var t in tokensQ) if (tokensCand.Contains(t)) match++;
            tokenCoverage = (double)match / tokensQ.Count;
            var add = tokenCoverage * 0.25;
            score += add; if (signals != null) signals["TokenCoverage"] = add;
        }
        else if (lowerPath.Contains(query)) { score += 0.15; if (signals != null) signals["CollapsedSubstring"] = 0.15; }

        // 1c. Collapsed substring fuzzy: if no token coverage and no direct substring (with spaces), try collapsed comparison
        if (tokenCoverage == 0)
        {
            var collapsedName = (fileName ?? string.Empty).Replace(" ", string.Empty);
            if (!string.IsNullOrEmpty(collapsedQuery) && collapsedName.Contains(collapsedQuery) && !fileName!.Equals(query, StringComparison.OrdinalIgnoreCase)) { score += 0.08; if (signals != null) signals["CollapsedSubstring"] = 0.08; }
        }

        int extraTokenCountForCandidate = 0; // track tokens not in query for later noise penalty
        if (tokensCand.Count > 0)
        {
            foreach (var t in tokensCand) if (!tokensQ.Contains(t)) extraTokenCountForCandidate++;
        }
        // 1b. Fuzzy token ratio (very lightweight): token overlap over union (if partial mismatch) – adds up to +0.10 (noise scaled)
        if (tokensQ.Count > 0 && tokensCand.Count > 0)
        {
            var union = new System.Collections.Generic.HashSet<string>(tokensCand, StringComparer.OrdinalIgnoreCase);
            foreach (var t in tokensQ) union.Add(t);
            int inter = 0; foreach (var t in tokensQ) if (tokensCand.Contains(t)) inter++;
            if (union.Count > 0)
            {
                var jaccard = (double)inter / union.Count;
                if (jaccard > 0 && jaccard < 1)
                {
                    double noiseFactor = 1.0;
                    if (extraTokenCountForCandidate >= 2) noiseFactor = 0.6;
                    if (extraTokenCountForCandidate >= 4) noiseFactor = 0.4;
                    var add = jaccard * 0.08 * noiseFactor; score += add; if (signals != null) signals["PartialTokenJaccard"] = add;
                }
            }
        }

        // 2. Exact filename match (strong) – additive but within reasonable cap. Alias equivalence considered.
        if (!string.IsNullOrEmpty(fileName))
        {
            if (fileName.Equals(query, StringComparison.OrdinalIgnoreCase)) { score += 0.30; if (signals != null) signals["FilenameExactOrPartial"] = 0.30; }
            else if (AliasEquivalent(query, fileName, out var alias)) { score += 0.22; if (signals != null) signals["AliasEquivalence"] = 0.22; }
            else if (tokenCoverage == 0 && fileName.Contains(query)) { score += 0.12; if (signals != null) signals["FilenameExactOrPartial"] = 0.12; }
        }

        // 2b. For Config/Data hits, allow directory-name alias equivalence to contribute (common pattern: query 'vscode' directory 'Code')
        if ((hit.Type == HitType.Config || hit.Type == HitType.Data) && !string.IsNullOrEmpty(dirName))
        {
            if (dirName.Equals(query, StringComparison.OrdinalIgnoreCase)) { score += 0.20; if (signals != null) signals["DirAlias"] = 0.20; }
            else if (AliasEquivalent(query, dirName!, out var dirAlias)) { score += 0.18; if (signals != null) signals["DirAlias"] = 0.18; }
        }

        // 3. Evidence-based boosts & synergies
        var ev = hit.Evidence;
        if (ev != null)
        {
            bool shortcut = ev.ContainsKey("Shortcut");
            bool process = ev.ContainsKey("ProcessId");
            double evidenceAdds = 0, evidenceSynergy = 0, evidencePenalties = 0;
            if (shortcut) { score += 0.10; evidenceAdds += 0.10; }
            if (process) { score += 0.08; evidenceAdds += 0.08; }
            if (shortcut && process) { score += 0.05; evidenceSynergy += 0.05; }
            if (ev.ContainsKey("where")) { score += 0.05; evidenceAdds += 0.05; }
            if (ev.ContainsKey("DirMatch")) { score += 0.06; evidenceAdds += 0.06; }
            if (ev.ContainsKey("ExeName")) { score += 0.04; evidenceAdds += 0.04; }
            if (ev.ContainsKey("AliasMatched")) { score += 0.14; evidenceAdds += 0.14; }
            if (ev.ContainsKey("BrokenShortcut")) { score -= 0.15; evidencePenalties -= 0.15; }
            if (signals != null)
            {
                if (evidenceAdds != 0) signals["EvidenceBoosts"] = evidenceAdds;
                if (evidenceSynergy != 0) signals["EvidenceSynergy"] = evidenceSynergy;
                if (evidencePenalties != 0) signals["EvidencePenalties"] = evidencePenalties;
            }
        }

        // 4. Path quality penalties (extend to installer caches & ephemeral roots) – stronger temp/staging demotion
        double pathPenalties = 0;
        bool tempLike = lowerPath.Contains("\\temp\\") || lowerPath.Contains("/temp/") || lowerPath.Contains("%temp%") || lowerPath.Contains("appdata\\local\\temp");
        if (tempLike) { score -= 0.18; pathPenalties -= 0.18; }
        if (lowerPath.Contains("\\installer\\") || lowerPath.EndsWith(".tmp.exe", StringComparison.OrdinalIgnoreCase)) { score -= 0.10; pathPenalties -= 0.10; }
        if (lowerPath.Contains("edgeupdate\\temp")) { score -= 0.06; pathPenalties -= 0.06; }
        if (lowerPath.Contains("\\temp\\winget\\") || lowerPath.Contains("/temp/winget/")) { score -= 0.15; pathPenalties -= 0.15; }
        if (signals != null && pathPenalties != 0) signals["PathPenalties"] = pathPenalties;

        // 4b. Token span tightness & noise penalty: contiguous coverage wins over spaced with many unrelated inserts
        if (tokensQ.Count > 1 && !string.IsNullOrEmpty(fileName))
        {
            var simplified = fileName.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
            var joinedInOrder = string.Join(string.Empty, tokensQ); // e.g., googlechrome
            bool contiguous = false;
            if (simplified.Contains(joinedInOrder, StringComparison.OrdinalIgnoreCase))
            {
                bool allPresent = true;
                foreach (var t in tokensQ) if (!simplified.Contains(t, StringComparison.OrdinalIgnoreCase)) { allPresent = false; break; }
                contiguous = allPresent;
            }

            // Count how many distinct extra tokens exist beyond query tokens in filename+dir tokens
            int extraTokens = 0;
            if (tokensCand.Count > 0)
            {
                foreach (var t in tokensCand)
                {
                    if (!tokensQ.Contains(t)) extraTokens++;
                }
            }

            if (contiguous)
            {
                score += 0.14; AddSignal(signals, "ContiguousSpan", 0.14);
                if (extraTokens > 2) { score -= 0.01; AddOrAccumulate(signals, "NoisePenalties", -0.01); }
            }
            else if (extraTokens > 1 && tokenCoverage < 1)
            {
                var sub = Math.Min(0.12, 0.02 * extraTokens);
                score -= sub; AddOrAccumulate(signals, "NoisePenalties", -sub);
            }
        }

        // 4c. Global noise penalty (post primary boosts) if excessive extra tokens without contiguous span or exact match
        if (extraTokenCountForCandidate >= 4 && tokenCoverage < 1)
        {
            var sub = Math.Min(0.06, 0.01 * extraTokenCountForCandidate);
            score -= sub; AddOrAccumulate(signals, "NoisePenalties", -sub);
        }

        // 5. Multi-source diminishing returns (harmonic series scaling) cap +0.18
        var sourceCount = hit.Source?.Length ?? 0;
        if (sourceCount > 1)
        {
            double harmonic = 0;
            for (int i = 2; i <= sourceCount; i++) harmonic += 1.0 / i;
            double norm = harmonic / 0.9; if (norm > 1) norm = 1;
            var add = norm * 0.18; score += add; if (signals != null) signals["MultiSource"] = add;
        }

        // 6. Type baseline weighting
        var typeAdd = hit.Type switch
        {
            HitType.Exe => 0.08,
            HitType.Config => 0.05,
            HitType.InstallDir => 0.04,
            HitType.Data => 0.03,
            _ => 0
        };
        score += typeAdd; if (signals != null && typeAdd != 0) signals["TypeBaseline"] = typeAdd;

        // 7. Fuzzy filename vs query Levenshtein (only when not exact / alias exact). Lightweight manual impl bounded length.
        if (!string.IsNullOrEmpty(fileName) && !fileName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            var fuzzy = FuzzyRatio(fileName, query); // 0..1 similarity
            if (fuzzy > 0.5 && tokenCoverage < 1) { var add = (fuzzy - 0.5) * 0.12; score += add; if (signals != null) signals["FuzzyLevenshtein"] = add; }
        }

        // 8. Post adjustments: mild reward for deeper token coverage precision (all tokens matched and exact file)
        if (tokenCoverage == 1 && !string.IsNullOrEmpty(fileName) && fileName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.05;
            if (signals != null) signals["ExactMatchBonus"] = 0.05;
        }

        // Clamp
        if (score > 1.0)
        {
            score = 1.0;
        }
        if (score < 0)
        {
            score = 0;
        }

        // (3) Penalize uninstaller/update-cache executables unless explicitly searched for uninstall
        if (hit.Type == HitType.Exe)
        {
            var fn = Safe(() => System.IO.Path.GetFileName(hit.Path)?.ToLowerInvariant()) ?? string.Empty;
            bool uninstallLike = fn.StartsWith("unins", StringComparison.Ordinal)
                                 || fn.Contains("uninstall", StringComparison.Ordinal)
                                 || fn.Contains("unins000", StringComparison.Ordinal)
                                 || fn.Contains("update-cache", StringComparison.Ordinal)
                                 || (fn.Contains("setup", StringComparison.Ordinal) && fn.EndsWith(".exe", StringComparison.Ordinal));
            if (uninstallLike && !query.Contains("uninstall"))
            {
                score -= 0.25;
                AddSignal(signals, "UninstallPenalty", -0.25);
                if (score < 0) score = 0;
            }
            // (5) Steam auxiliary dampening: if query is 'steam' and filename contains helper patterns (webhelper, errorreporter, service, xboxutil, sysinfo)
            if (query == "steam")
            {
                if (fn.Contains("webhelper") || fn.Contains("errorreporter") || fn.Contains("service") || fn.Contains("xboxutil") || fn.Contains("sysinfo") || fn.Contains("steamservice"))
                {
                    score -= 0.18;
                    AddSignal(signals, "SteamAuxPenalty", -0.18);
                    if (score < 0) score = 0;
                }
            }
        }

        // (4) Cross-app FL Cloud Plugins suppression
        if (lowerPath.Contains("fl cloud plugins"))
        {
            bool related = query.Contains("fl") || query.Contains("cloud") || query.Contains("plugin");
            if (!related)
            {
                score -= 0.35;
                AddSignal(signals, "PluginSuppression", -0.35);
                if (score < 0) score = 0;
            }
        }

        // (4d) Cache / transient artifact demotion (Code Cache, VideoDecodeStats, update-cache, Winget temp version folders)
        if (lowerPath.Contains("code cache") || lowerPath.Contains("video\\decode") || lowerPath.Contains("videodecodestats") || lowerPath.Contains("video\\decodestats"))
        {
            score -= 0.25;
            AddOrAccumulate(signals, "CacheArtifactPenalty", -0.25);
            if (score < 0) score = 0;
        }
        if (lowerPath.Contains("\\update-cache\\") || lowerPath.EndsWith("\\update-cache", StringComparison.OrdinalIgnoreCase))
        {
            score -= 0.22;
            AddOrAccumulate(signals, "CacheArtifactPenalty", -0.22);
            if (score < 0) score = 0;
        }
        if (lowerPath.Contains("\\temp\\winget\\") || lowerPath.Contains("winget."))
        {
            score -= 0.10;
            AddOrAccumulate(signals, "CacheArtifactPenalty", -0.10);
            if (score < 0) score = 0;
        }
        AddSignal(signals, "Total", score);
        return score;
    }

    private static string? Safe(Func<string?> f) { try { return f(); } catch { return null; } }

    private static System.Collections.Generic.HashSet<string> Tokenize(string? value)
    {
        var set = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return set;
        }
        var parts = value.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.Length == 0) { continue; }
            set.Add(t);
        }
        return set;
    }

    private static readonly char[] Separators = { ' ', '-', '_', '.' };

    private static void AddSignal(System.Collections.Generic.Dictionary<string, double>? map, string key, double value)
    {
        if (map == null) { return; }
        map[key] = value;
    }

    private static void AddOrAccumulate(System.Collections.Generic.Dictionary<string, double>? map, string key, double delta)
    {
        if (map == null) { return; }
        if (map.TryGetValue(key, out var existing)) { map[key] = existing + delta; }
        else { map[key] = delta; }
    }

    // Expands a token by splitting camelCase/PascalCase and numeric boundaries; returns additional fragments.
    private static System.Collections.Generic.IEnumerable<string> ExpandToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 4)
        {
            yield break; // skip very short
        }
        // CamelCase: split before capitals (except first)
        var segments = new System.Collections.Generic.List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i];
            if (i > 0 && char.IsUpper(c) && (current.Length > 0))
            {
                segments.Add(current.ToString());
                current.Clear();
            }
            current.Append(c);
        }
        if (current.Length > 0)
        {
            segments.Add(current.ToString());
        }
        if (segments.Count > 1)
        {
            foreach (var s in segments)
            {
                var ls = s.ToLowerInvariant();
                if (ls.Length > 1)
                {
                    yield return ls;
                }
            }
        }
        // Additionally split numeric boundaries (e.g., app2go -> app, 2, go)
        var numBuf = new System.Text.StringBuilder();
        var alphaBuf = new System.Text.StringBuilder();
        void FlushAlpha(System.Collections.Generic.List<string> output)
        {
            if (alphaBuf.Length > 1)
            {
                var v = alphaBuf.ToString().ToLowerInvariant();
                if (v != token)
                {
                    output.Add(v);
                }
            }
            alphaBuf.Clear();
        }
        void FlushNum(System.Collections.Generic.List<string> output)
        {
            if (numBuf.Length > 0)
            {
                var v = numBuf.ToString();
                if (v != token)
                {
                    output.Add(v);
                }
            }
            numBuf.Clear();
        }
        var extra = new System.Collections.Generic.List<string>();
        foreach (var c in token)
        {
            if (char.IsDigit(c))
            {
                if (alphaBuf.Length > 0)
                {
                    FlushAlpha(extra);
                }
                numBuf.Append(c);
            }
            else
            {
                if (numBuf.Length > 0)
                {
                    FlushNum(extra);
                }
                alphaBuf.Append(c);
            }
        }
        if (alphaBuf.Length > 0)
        {
            FlushAlpha(extra);
        }
        if (numBuf.Length > 0)
        {
            FlushNum(extra);
        }
        foreach (var e in extra)
        {
            yield return e;
        }
    }

    // Lightweight Levenshtein similarity ratio (1 - distance/maxLen)
    private static double FuzzyRatio(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }
        // Cap to avoid excessive cost on very long paths (only filenames here so fine)
        var la = a.Length; var lb = b.Length;
        var d = new int[la + 1, lb + 1];
        for (var i = 0; i <= la; i++)
        {
            d[i, 0] = i;
        }
        for (var j = 0; j <= lb; j++)
        {
            d[0, j] = j;
        }
        for (var i = 1; i <= la; i++)
        {
            for (var j = 1; j <= lb; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var del = d[i - 1, j] + 1;
                var ins = d[i, j - 1] + 1;
                var sub = d[i - 1, j - 1] + cost;
                var min = del < ins ? del : ins;
                if (sub < min)
                {
                    min = sub;
                }
                d[i, j] = min;
            }
        }
        var dist = d[la, lb];
        double maxLen = Math.Max(la, lb);
        var ratio = 1.0 - (dist / maxLen);
        if (ratio < 0)
        {
            ratio = 0;
        }
        if (ratio > 1)
        {
            ratio = 1;
        }
        return ratio;
    }
    private static ScoreBreakdown BuildBreakdown(System.Collections.Generic.Dictionary<string, double> map, double total)
    {
        double Get(string k)
        {
            return map.TryGetValue(k, out var v) ? v : 0;
        }
        return new ScoreBreakdown(
            Get("TokenCoverage"),
            Get("CollapsedSubstring"),
            Get("PartialTokenJaccard"),
            Get("FilenameExactOrPartial"),
            Get("AliasEquivalence"),
            Get("DirAlias"),
            Get("EvidenceBoosts"),
            Get("EvidenceSynergy"),
            Get("EvidencePenalties"),
            Get("PathPenalties"),
            Get("ContiguousSpan"),
            Get("NoisePenalties"),
            Get("MultiSource"),
            Get("TypeBaseline"),
            Get("FuzzyLevenshtein"),
            Get("ExactMatchBonus"),
            Get("UninstallPenalty"),
            Get("SteamAuxPenalty"),
            Get("PluginSuppression"),
            Get("CacheArtifactPenalty"),
            Get("PairingBoost"),
            Get("GenericDirPenalty"),
            Get("DirMinFloor"),
            Get("OrphanProbeAdjustments"),
            Get("VariantSiblingBoost"),
            total,
            map
        );
    }
}
