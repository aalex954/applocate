using System;using System.Diagnostics;using System.IO;using System.Linq;using System.Text.Json;using Xunit;

namespace AppLocate.Cli.Tests;

public class TypeFilterTests
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

    private static int CountType(string json, int enumUnderlying)
    {
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.EnumerateArray().Count(el => el.GetProperty("type").GetInt32() == enumUnderlying); } catch { return 0; }
    }

    [Fact]
    public void ExeFilter_OnlyExeHits()
    {
        var (code, json, err) = Run("code", "--json", "--exe", "--all", "--limit", "50", "--refresh-index");
        Assert.Contains(code, new[]{0,1});
        Assert.True(string.IsNullOrWhiteSpace(err), $"stderr: {err}");
        if (code == 0)
        {
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement.EnumerateArray().ToList();
            Assert.All(arr, el => Assert.Equal(1, el.GetProperty("type").GetInt32())); // HitType.Exe underlying = 1
        }
    }

    [Fact]
    public void MultiFilter_ExeAndInstall()
    {
        var (code, json, err) = Run("code", "--json", "--exe", "--install-dir", "--all", "--limit", "100", "--refresh-index");
        Assert.Contains(code, new[]{0,1});
        Assert.True(string.IsNullOrWhiteSpace(err));
        if (code == 0)
        {
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement.EnumerateArray().ToList();
            // Ensure only allowed types appear
            Assert.All(arr, el => { var t = el.GetProperty("type").GetInt32(); Assert.Contains(t, new[]{0,1}); });
        }
    }
}
