using System;
using AppLocate.Core.Models;

namespace AppLocate.Core.Ranking;

/// <summary>Heuristic ranking engine (interim). Combines path/name match quality, evidence signals, and multi-source strength.</summary>
internal static class Ranker
{
    public static double Score(string normalizedQuery, AppHit hit)
    {
        if (string.IsNullOrEmpty(normalizedQuery)) return 0;
        var path = hit.Path ?? string.Empty;
        var lowerPath = path.ToLowerInvariant();
        var query = normalizedQuery.ToLowerInvariant();
        double score = 0;

        // Base match: substring in path or evidence-provided name.
        if (lowerPath.Contains(query))
        {
            score += 0.35; // base path presence
        }

        // Filename exact (without extension) match boost.
        try
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(path)?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(fileName))
            {
                if (fileName.Equals(query, StringComparison.OrdinalIgnoreCase)) score += 0.35; // strong exact
                else if (fileName.Contains(query)) score += 0.15; // partial name
            }
        }
        catch { }

        // Evidence-based boosts.
        var ev = hit.Evidence;
        if (ev != null)
        {
            if (ev.ContainsKey("Shortcut")) score += 0.10; // Start Menu shortcut
            if (ev.ContainsKey("ProcessId")) score += 0.08; // actively running
            if (ev.ContainsKey("where")) score += 0.05; // resolved via PATH/where
            if (ev.ContainsKey("DirMatch")) score += 0.06; // directory name matched
            if (ev.ContainsKey("ExeName")) score += 0.04; // exe-name matched in heuristic search
        }

        // Multi-source strength: diminishing returns after first.
        var sourceCount = hit.Source?.Length ?? 0;
        if (sourceCount > 1)
        {
            // Add up to +0.15 distributed logarithmically.
            score += Math.Min(0.15, Math.Log10(sourceCount + 1) * 0.10);
        }

        // Type weighting: exe slightly higher than install dir; config/data will later have different baselines.
        switch (hit.Type)
        {
            case HitType.Exe: score += 0.07; break;
            case HitType.InstallDir: score += 0.02; break;
            case HitType.Config: score += 0.03; break;
            case HitType.Data: score += 0.01; break;
        }

        // Clamp and normalize upper bound.
        if (score > 1.0) score = 1.0;
        if (score < 0) score = 0;
        return score;
    }
}
