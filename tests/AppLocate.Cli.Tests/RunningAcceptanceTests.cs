using System;using System.Diagnostics;using System.IO;using System.Linq;using System.Text.Json;using Xunit;

namespace AppLocate.Cli.Tests;

public class RunningAcceptanceTests
{
    private static (string file, bool directExe) LocateCli()
    {
        var asmPath = typeof(AppLocate.Cli.Program).Assembly.Location;
        var exeCandidate = Path.ChangeExtension(asmPath, ".exe");
        if (File.Exists(exeCandidate)) return (exeCandidate, true);
        return (asmPath, false);
    }

    private static (int exitCode, string stdout, string stderr) Run(params string[] args)
    {
        var (cli, direct) = LocateCli();
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (direct) { psi.FileName = cli; foreach (var a in args) psi.ArgumentList.Add(a); }
        else { psi.FileName = "dotnet"; psi.ArgumentList.Add(cli); foreach (var a in args) psi.ArgumentList.Add(a); }
        var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEnd();
        var se = p.StandardError.ReadToEnd();
        p.WaitForExit(20000);
        return (p.ExitCode, so, se);
    }

    [Fact]
    public void RunningFlag_FindsLaunchedProcessExe()
    {
        // Launch a lightweight child process with a stable executable name (e.g., notepad if available) else fall back to powershell itself.
        // Prefer system notepad because its filename includes 'notepad'; query will approximate.
        Process? child = null;
        string query;
        try
        {
            var notepadPath = Environment.ExpandEnvironmentVariables("%SystemRoot%/System32/notepad.exe");
            if (File.Exists(notepadPath))
            {
                child = Process.Start(new ProcessStartInfo(notepadPath){ UseShellExecute=false });
                query = "notepad";
            }
            else
            {
                var pwsh = Environment.ProcessPath!; // current dotnet or testhost; fall back to its process name
                child = Process.GetCurrentProcess();
                query = child.ProcessName.ToLowerInvariant();
            }
            System.Threading.Thread.Sleep(250); // brief delay to ensure process registered
            var (code, json, err) = Run(query, "--json", "--running", "--limit", "200", "--refresh-index");
            Assert.Contains(code, new[]{0,1});
            Assert.True(string.IsNullOrWhiteSpace(err), $"stderr: {err}");
            if (code == 0 && child != null && !child.HasExited)
            {
                using var doc = JsonDocument.Parse(json);
                var hits = doc.RootElement.EnumerateArray().ToList();
                var childExe = SafePath(() => child.MainModule?.FileName);
                var childName = SafePath(() => Path.GetFileNameWithoutExtension(childExe) ?? child.ProcessName)?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(childName))
                {
                    bool nameFound = hits.Any(h => {
                        var p = h.GetProperty("path").GetString();
                        if (string.IsNullOrEmpty(p)) return false;
                        var fn = Path.GetFileNameWithoutExtension(p)?.ToLowerInvariant();
                        return fn == childName;
                    });
                    Assert.True(nameFound, $"Expected running process name '{childName}' in results. Raw hit count={hits.Count}");
                }
            }
        }
        finally
        {
            try { if (child != null && !child.HasExited && child.ProcessName.Equals("notepad", StringComparison.OrdinalIgnoreCase)) child.Kill(); } catch { }
        }
    }

    private static string? SafePath(Func<string?> f) { try { return f(); } catch { return null; } }
}
