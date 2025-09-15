using System.Diagnostics;
using System.Text.Json;

namespace AppLocate.Cli.Tests {
    public class RunningAcceptanceTests {
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
            _ = p.WaitForExit(20000);
            return (p.ExitCode, so, se);
        }

        [Fact]
        public void RunningFlag_FindsLaunchedProcessExe() {
            // Launch a lightweight headless process (cmd.exe /c timeout) to avoid GUI popups in CI.
            // Fallback to current process name if cmd unavailable (unlikely on Windows GitHub runner).
            Process? child = null;
            string query;
            try {
                var cmdPath = Environment.ExpandEnvironmentVariables("%SystemRoot%/System32/cmd.exe");
                if (File.Exists(cmdPath)) {
                    child = Process.Start(new ProcessStartInfo(cmdPath, "/c timeout /t 2 >NUL") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                    query = "cmd"; // executable base name
                }
                else {
                    child = Process.GetCurrentProcess();
                    query = child.ProcessName.ToLowerInvariant();
                }
                Thread.Sleep(300); // ensure OS registers process
                var (code, json, err) = Run(query, "--json", "--running", "--limit", "200");
                Assert.Contains(code, new[] { 0, 1 });
                Assert.True(string.IsNullOrWhiteSpace(err), $"stderr: {err}");
                if (code == 0 && child != null && !child.HasExited) {
                    using var doc = JsonDocument.Parse(json);
                    var hits = doc.RootElement.EnumerateArray().ToList();
                    var childExe = SafePath(() => child.MainModule?.FileName);
                    var childName = SafePath(() => Path.GetFileNameWithoutExtension(childExe) ?? child.ProcessName)?.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(childName)) {
                        var nameFound = hits.Any(h => {
                            var p = h.GetProperty("path").GetString();
                            if (string.IsNullOrEmpty(p)) {
                                return false;
                            }

                            var fn = Path.GetFileNameWithoutExtension(p)?.ToLowerInvariant();
                            return fn == childName;
                        });
                        Assert.True(nameFound, $"Expected running process name '{childName}' in results. Raw hit count={hits.Count}");
                    }
                }
            }
            finally {
                try { if (child != null && !child.HasExited) { child.Kill(); } } catch { }
            }
        }

        private static string? SafePath(Func<string?> f) { try { return f(); } catch { return null; } }
    }
}
