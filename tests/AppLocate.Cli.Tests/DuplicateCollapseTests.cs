using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AppLocate.Cli.Tests {
    public class DuplicateCollapseTests {
        private static (string file, bool directExe) LocateCli() {
            var asmPath = typeof(Program).Assembly.Location;
            var exeCandidate = Path.ChangeExtension(asmPath, ".exe");
            if (File.Exists(exeCandidate)) {
                return (exeCandidate, true);
            }

            return (asmPath, false);
        }

        private static (int exitCode, string stdout, string stderr) Run(params string[] args) {
            var (cli, direct) = LocateCli();
            var psi = new ProcessStartInfo {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (direct) {
                psi.FileName = cli; foreach (var a in args) {
                    psi.ArgumentList.Add(a);
                }
            }
            else {
                psi.FileName = "dotnet"; psi.ArgumentList.Add(cli); foreach (var a in args) {
                    psi.ArgumentList.Add(a);
                }
            }
            var p = Process.Start(psi)!;
            var so = p.StandardOutput.ReadToEnd();
            var se = p.StandardError.ReadToEnd();
            _ = p.WaitForExit(15000);
            return (p.ExitCode, so, se);
        }

        // This test relies on the new path-level consolidation introduced after initial duplicate report.
        // Strategy: run a query that is likely to yield multiple sources for the same path ("code" common case), request --all
        // and assert no duplicate Type+Path combos appear in text output. If environment lacks VS Code, test is skipped.
        [Fact]
        public void AllResults_NoDuplicateTypePathPairs() {
            var (code, stdout, stderr) = Run("code", "--all", "--limit", "100");
            Assert.True(string.IsNullOrWhiteSpace(stderr), $"stderr not empty: {stderr}");
            if (code != 0) {
                // Environment may not have VS Code installed; skip.
                return; // treat as implicit skip (avoid Skip exception dependency)
            }
            // Parse lines: format like "[0.83] Exe C:\Path\To\Code.exe" OR "Exe C:\Path" (when confidence omitted in some styles)
            // We'll extract type + path via regex.
            var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var rx = new Regex(@"^(?:\[[0-9]+\.[0-9]+\]\s+)?(Exe|InstallDir|Config|Data)\s+(.+)$", RegexOptions.IgnoreCase);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines) {
                var m = rx.Match(line.Trim());
                if (!m.Success) {
                    continue; // ignore non-hit lines
                }

                var type = m.Groups[1].Value;
                var path = m.Groups[2].Value.Trim();
                var key = type + "|" + path;
                Assert.True(set.Add(key), $"Duplicate Type+Path pair found in output: {key}\nFull output:\n{stdout}");
            }
        }
    }
}
