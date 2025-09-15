namespace AppLocate.Core.Rules {
    /// <summary>Placeholder rules engine. Will parse YAML and produce config/data paths.</summary>
    /// <summary>
    /// Lightweight YAML-like rules processor that loads app-specific config/data expansion rules.
    /// Format intentionally minimal to avoid external dependencies; upgrade path to full YAML parser preserved.
    /// </summary>
    public sealed class RulesEngine {
        /// <summary>
        /// Loads a very small subset of YAML supporting a list of items each with:
        /// - match.anyOf: ["Name", "Alt"]
        /// - config: ["%APPDATA%/..."]
        /// - data: ["%LOCALAPPDATA%/..." ]
        /// This avoids a heavy YAML dependency; upgrade to YamlDotNet later if schema grows.
        /// </summary>
        public static Task<IReadOnlyList<ResolvedRule>> LoadAsync(string file, CancellationToken ct) {
            if (!File.Exists(file)) {
                return Task.FromResult<IReadOnlyList<ResolvedRule>>([]);
            }

            var lines = File.ReadAllLines(file);
            var rules = new List<ResolvedRule>();
            List<string>? currentMatch = null;
            List<string>? currentConfig = null;
            List<string>? currentData = null;
            static List<string> ParseInlineArray(string line) {
                // expects ["a", "b"] or ["a"]
                var start = line.IndexOf('[');
                var end = line.IndexOf(']');
                if (start < 0 || end < start) {
                    return [];
                }

                var inner = line.Substring(start + 1, end - start - 1);
                return [.. inner.Split(',').Select(s => s.Trim().Trim('"')).Where(s => s.Length > 0)];
            }
            void Flush() {
                if (currentMatch != null) {
                    rules.Add(new ResolvedRule([.. currentMatch], [.. (currentConfig ?? [])], [.. (currentData ?? [])]));
                    currentMatch = null; currentConfig = null; currentData = null;
                }
            }
            foreach (var raw in lines) {
                if (ct.IsCancellationRequested) {
                    break;
                }
                var line = raw.Trim();
                if ((line.Length > 0 && line[0] == '#') || line.Length == 0) {
                    continue;
                }
                if (line.StartsWith("- match", StringComparison.Ordinal)) {
                    Flush();
                    continue;
                }
                if (line.StartsWith("anyOf:", StringComparison.Ordinal)) {
                    currentMatch = ParseInlineArray(line);
                    continue;
                }
                if (line.StartsWith("config:", StringComparison.Ordinal)) {
                    currentConfig = ParseInlineArray(line);
                    continue;
                }
                if (line.StartsWith("data:", StringComparison.Ordinal)) {
                    currentData = ParseInlineArray(line);
                    continue;
                }
            }
            Flush();
            return Task.FromResult<IReadOnlyList<ResolvedRule>>(rules);
        }
    }

    /// <summary>
    /// A resolved rule describing match tokens and resulting config/data path templates.
    /// </summary>
    /// <param name="MatchAnyOf">Tokens (case-insensitive) that, if any match application name or existing hits, activate the rule.</param>
    /// <param name="Config">Config path templates (environment variables are expanded at evaluation time).</param>
    /// <param name="Data">Data path templates (environment variables are expanded at evaluation time).</param>
    public sealed record ResolvedRule(string[] MatchAnyOf, string[] Config, string[] Data);
}
