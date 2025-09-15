using System.Diagnostics;
using Xunit;

namespace AppLocate.Cli.Tests;

public class CliDeterministicTests
{
    private static (string file, bool directExe) LocateCli()
    {
        var asmPath = typeof(AppLocate.Cli.Program).Assembly.Location;
        var exeCandidate = System.IO.Path.ChangeExtension(asmPath, ".exe");
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
        if (direct)
        {
            psi.FileName = cli;
            foreach (var a in args) psi.ArgumentList.Add(a);
        }
        else
        {
            psi.FileName = "dotnet";
            psi.ArgumentList.Add(cli);
            foreach (var a in args) psi.ArgumentList.Add(a);
        }
        var p = Process.Start(psi)!;
        string so = p.StandardOutput.ReadToEnd();
        string se = p.StandardError.ReadToEnd();
        p.WaitForExit(10000);
        return (p.ExitCode, so, se);
    }

    [Fact]
    public void Help_ShowsUsageAndExit0()
    {
        var (code, stdout, stderr) = Run("--help");
        Assert.Equal(0, code);
        Assert.Contains("applocate <query>", stdout);
        Assert.Empty(stderr.Trim());
    }

    [Fact]
    public void MissingQuery_Exit2()
    {
        var (code, _out, err) = Run();
        Assert.Equal(2, code);
        // Accept either our custom message or System.CommandLine's grammar message
        Assert.True(err.Contains("Missing <query>") || err.Contains("Required argument missing"), $"stderr: {err}");
    }

    [Fact]
    public void InvalidLimit_Exit2()
    {
        var (code, _out, err) = Run("foo", "--limit", "-5");
        Assert.Equal(2, code);
        Assert.Contains("--limit must be >= 0", err);
    }

    [Fact]
    public void ConfidenceOutOfRange_Exit2()
    {
        var (code, _out, err) = Run("foo", "--confidence-min", "1.5");
        Assert.Equal(2, code);
        Assert.Contains("--confidence-min", err);
    }

    [Fact]
    public void TimeoutTooLarge_Exit2()
    {
        var (code, _out, err) = Run("foo", "--timeout", "999");
        Assert.Equal(2, code);
        Assert.Contains("--timeout too large", err);
    }

    [Fact]
    public void SentinelTreatsDashesAsQuery()
    {
        var (code, _out, err) = Run("--", "--strange-name--app");
        // Probably no matches; exit 1 or 0 depending on local environment; accept 0/1.
        Assert.Contains(code, new[] { 0, 1 });
        Assert.True(string.IsNullOrEmpty(err.Trim()), $"Unexpected stderr: {err}");
    }

    [Fact]
    public void UnknownQuery_Exit1()
    {
        var (code, _out, err) = Run("definitely-not-an-installed-app-zzzz");
        // Expect cache miss path -> exit 1
        Assert.Equal(1, code);
        Assert.True(string.IsNullOrEmpty(err.Trim()), $"stderr not empty: {err}");
    }
}
