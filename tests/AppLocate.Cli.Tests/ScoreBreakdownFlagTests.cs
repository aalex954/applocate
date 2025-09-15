using System.Diagnostics;
using System.Text.Json;

namespace AppLocate.Cli.Tests {
    public class ScoreBreakdownFlagTests {
        private static readonly int[] AcceptExitCodes = [0, 1];
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

        [Fact]
        public void Json_Includes_Breakdown_When_Flag() {
            var (code, json, err) = Run("code", "--json", "--score-breakdown", "--limit", "10");
            Assert.Contains(code, AcceptExitCodes);
            Assert.True(string.IsNullOrWhiteSpace(err), $"stderr: {err}");
            if (code == 0) {
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement.EnumerateArray().ToList();
                if (arr.Count == 0) {
                    return; // environment may have no matches
                }
                // At least one element should contain a 'breakdown' object with expected numeric properties
                var anyWith = arr.Any(e => e.TryGetProperty("breakdown", out var b) && b.ValueKind == JsonValueKind.Object && b.TryGetProperty("total", out _));
                Assert.True(anyWith, "Expected at least one hit with breakdown when flag used");
            }
        }

        [Fact]
        public void Json_Excludes_Breakdown_When_NoFlag() {
            var (code, json, err) = Run("code", "--json", "--limit", "10");
            Assert.Contains(code, AcceptExitCodes);
            Assert.True(string.IsNullOrWhiteSpace(err), $"stderr: {err}");
            if (code == 0) {
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement.EnumerateArray().ToList();
                foreach (var e in arr) {
                    Assert.False(e.TryGetProperty("breakdown", out _), "Did not expect breakdown without flag");
                }
            }
        }

        [Fact]
        public void Text_Mode_Shows_Breakdown_Line() {
            var (code, stdout, err) = Run("code", "--score-breakdown", "--limit", "5", "--no-color");
            Assert.Contains(code, AcceptExitCodes);
            Assert.True(string.IsNullOrWhiteSpace(err), $"stderr: {err}");
            if (code == 0) {
                // Look for at least one 'breakdown:' line
                var hasLine = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                                     .Any(l => l.TrimStart().StartsWith("breakdown:", StringComparison.OrdinalIgnoreCase));
                Assert.True(hasLine, "Expected breakdown line in text mode with flag");
            }
        }
    }
}
