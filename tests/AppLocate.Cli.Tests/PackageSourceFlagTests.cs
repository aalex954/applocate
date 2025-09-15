using System.Diagnostics;

namespace AppLocate.Cli.Tests {
    public class PackageSourceFlagTests {
        private static (string file, bool directExe) LocateCli() {
            var asmPath = typeof(Program).Assembly.Location; var exeCandidate = Path.ChangeExtension(asmPath, ".exe"); if (File.Exists(exeCandidate)) {
                return (exeCandidate, true);
            }

            return (asmPath, false);
        }

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
        public void TextOutput_IncludesPkgAndSrc_WhenFlag() {
            var (code, stdout, stderr) = Run("code", "--limit", "3", "--package-source");
            Assert.Contains(code, new[] { 0, 1 });
            Assert.True(string.IsNullOrWhiteSpace(stderr), $"stderr: {stderr}");
            if (code == 0) {
                var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                // At least one line should contain (pkg= and src=
                Assert.Contains(lines, l => l.Contains("(pkg=") && l.Contains("src="));
            }
        }

        [Fact]
        public void CsvOutput_AddsSourcesColumn() {
            var (code, stdout, stderr) = Run("code", "--csv", "--limit", "3", "--package-source");
            Assert.Contains(code, new[] { 0, 1 });
            Assert.True(string.IsNullOrWhiteSpace(stderr));
            if (code == 0) {
                var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                Assert.True(lines.Length >= 2, "Expected header + at least one row");
                Assert.Equal("Type,Scope,Path,Version,PackageType,Sources,Confidence", lines[0]);
            }
        }

        [Fact]
        public void CsvOutput_NoSourcesColumnWithoutFlag() {
            var (code, stdout, stderr) = Run("code", "--csv", "--limit", "3");
            Assert.Contains(code, new[] { 0, 1 });
            Assert.True(string.IsNullOrWhiteSpace(stderr));
            if (code == 0) {
                var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                Assert.True(lines.Length >= 2);
                Assert.Equal("Type,Scope,Path,Version,PackageType,Confidence", lines[0]);
            }
        }
    }
}
