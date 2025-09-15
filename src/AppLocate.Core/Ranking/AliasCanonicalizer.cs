using System.Collections.Concurrent;

namespace AppLocate.Core.Ranking {
    /// <summary>
    /// Lightweight alias canonicalization service. Uses predefined, data-driven clusters (no per-app bespoke logic) to map
    /// variant forms (e.g., "vscode", "visual studio code") to a canonical representative ("code").
    /// Future enhancement: extend via external rule packs or dynamic clustering of observed names.
    /// </summary>
    internal static class AliasCanonicalizer {
        // NOTE: All entries lowercase; first element of each cluster chosen as canonical.
        private static readonly string[][] _clusters =
        [
            ["code", "vscode", "visual studio code"],
            ["chrome", "google chrome"],
            ["edge", "microsoft edge"],
            ["notepad++", "notepadpp", "npp"],
            ["powershell", "pwsh"],
            ["oh-my-posh", "oh my posh", "ohmyposh", "oh_my_posh", "jandedobbeleer.ohmyposh"],
            // Windows Terminal (App Execution Alias: wt.exe)
            ["wt", "windows terminal", "wt.exe", "microsoft windows terminal"],
        ];

        private static readonly ConcurrentDictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

        static AliasCanonicalizer() {
            foreach (var cluster in _clusters) {
                if (cluster.Length == 0) { continue; }
                var canonical = cluster[0];
                foreach (var variant in cluster) {
                    _map[variant] = canonical;
                }
            }
        }

        /// <summary>
        /// Canonicalizes a raw user query. Returns lowercase, token-trimmed canonical form and indicates whether a
        /// transformation occurred (variant -> canonical).
        /// </summary>
        public static string Canonicalize(string rawQuery, out bool changed) {
            if (string.IsNullOrWhiteSpace(rawQuery)) { changed = false; return string.Empty; }
            var normalized = string.Join(' ', rawQuery.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (_map.TryGetValue(normalized, out var canonical)) {
                changed = !string.Equals(normalized, canonical, StringComparison.OrdinalIgnoreCase);
                return canonical;
            }
            changed = false;
            return normalized;
        }
    }
}
