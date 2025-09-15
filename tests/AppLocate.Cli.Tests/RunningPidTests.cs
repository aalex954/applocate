using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace AppLocate.Cli.Tests;

public class RunningPidTests
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
        p.WaitForExit(15000);
        return (p.ExitCode, so, se);
    }

    [Fact]
    public void RunningFlag_DoesNotError()
    {
        var (code, _out, err) = Run("code", "--json", "--running", "--limit", "5");
        Assert.Contains(code, new[] { 0, 1 });
        Assert.True(string.IsNullOrWhiteSpace(err), $"stderr: {err}");
    }

    [Fact]
    public void PidFlag_AddsProcessPath()
    {
        // Target current process (test runner) to guarantee a valid PID.
        var current = Process.GetCurrentProcess();
        var (code, json, err) = Run(current.ProcessName.ToLowerInvariant(), "--json", "--pid", current.Id.ToString(), "--limit", "50");
        Assert.Contains(code, new[] { 0, 1 });
        Assert.True(string.IsNullOrWhiteSpace(err), $"stderr: {err}");
        if (code == 0)
        {
            using var doc = JsonDocument.Parse(json);
            var hits = doc.RootElement.EnumerateArray().ToList();
            bool hasProcExe = hits.Any(h => h.GetProperty("path").GetString()!.Equals(current.MainModule?.FileName, StringComparison.OrdinalIgnoreCase));
            Assert.True(hasProcExe, "Expected process exe path from --pid injection");
        }
    }

    [Fact]
    public void InvalidPid_Exit2()
    {
        var (code, _out, err) = Run("code", "--json", "--pid", "-5");
        Assert.Equal(2, code);
        Assert.Contains("--pid must be > 0", err);
    }
}
