using System.Diagnostics;

namespace AppLocate.Cli.Tests {
    public class ThreadsOptionTests {
        private static readonly int[] AcceptExitCodes = [0, 1];
        private static (string file, bool directExe) LocateCli() { var asmPath = typeof(Program).Assembly.Location; var exeCandidate = Path.ChangeExtension(asmPath, ".exe"); if (File.Exists(exeCandidate)) { return (exeCandidate, true); } return (asmPath, false); }

        private static (int exitCode, string stdout, string stderr) Run(params string[] args) {
            var (cli, direct) = LocateCli();
            var psi = new ProcessStartInfo { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
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
            var p = Process.Start(psi)!; var so = p.StandardOutput.ReadToEnd(); var se = p.StandardError.ReadToEnd(); _ = p.WaitForExit(15000); return (p.ExitCode, so, se);
        }

        [Fact]
        public void ThreadsOne_SucceedsOrNoHits() {
            var (code, _, err) = Run("code", "--json", "--threads", "1", "--limit", "2");
            Assert.Contains(code, AcceptExitCodes);
            Assert.True(string.IsNullOrWhiteSpace(err), $"stderr: {err}");
        }

        [Fact]
        public void ThreadsLarge_SucceedsOrNoHits() {
            var (code, _, err) = Run("code", "--json", "--threads", "8", "--limit", "2");
            Assert.Contains(code, AcceptExitCodes);
            Assert.True(string.IsNullOrWhiteSpace(err), $"stderr: {err}");
        }

        [Fact]
        public void ThreadsInvalid_Exit2() {
            var (code, _, err) = Run("code", "--json", "--threads", "0");
            Assert.Equal(2, code);
            Assert.Contains("--threads must be > 0", err);
        }
    }
}
