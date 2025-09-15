using System.Text.RegularExpressions;

namespace AppLocate.Cli.Tests {
    public class RulePackExpansionTests {
        [Fact]
        public void RulePack_HasAtLeastFiftyEntries_AndCoreAppsPresent() {
            // Attempt to resolve rules file relative to repo root (two levels up from test bin usually)
            var cwd = Directory.GetCurrentDirectory();
            var probe = cwd;
            string? path = null;
            for (var i = 0; i < 6 && probe != null; i++) {
                var candidate = Path.Combine(probe, "rules", "apps.default.yaml");
                if (File.Exists(candidate)) { path = candidate; break; }
                probe = Path.GetDirectoryName(probe);
            }
            Assert.True(path != null, "rules/apps.default.yaml missing");
            var text = File.ReadAllText(path);
            // Count occurrences of top-level '- match:' lines
            var count = Regex.Matches(text, "^- match:", RegexOptions.Multiline).Count;
            Assert.True(count >= 50, $"Expected >=50 rules, found {count}");
            string[] spot = ["Visual Studio", "GitHub Desktop", "Gradle", "Rust", "Helm", "PowerToys", "Bazel", "MiKTeX"];
            foreach (var token in spot) {
                Assert.Contains(token, text);
            }
        }
    }
}