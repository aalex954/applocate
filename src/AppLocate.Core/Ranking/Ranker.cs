using System;
using AppLocate.Core.Models;

namespace AppLocate.Core.Ranking;

/// <summary>Placeholder ranking engine. Will be replaced with full fuzzy scoring rules.</summary>
internal static class Ranker
{
    public static double Score(string normalizedQuery, AppHit hit)
    {
        if (string.IsNullOrEmpty(normalizedQuery)) return 0;
        var path = hit.Path ?? string.Empty;
        return path.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ? 0.5 : 0.1;
    }
}
