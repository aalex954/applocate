using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace AppLocate.Cli.Tests;

public class EvidenceFlagTests
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
    public void EvidenceFlag_AddsEvidenceObjects()
    {
        var (codeWith, jsonWith, errWith) = Run("code", "--json", "--evidence", "--limit", "20");
        Assert.Contains(codeWith, new[] { 0, 1 });
        Assert.True(string.IsNullOrWhiteSpace(errWith), $"stderr: {errWith}");
        var (codeNo, jsonNo, errNo) = Run("code", "--json", "--limit", "20");
        Assert.Contains(codeNo, new[] { 0, 1 });
        Assert.True(string.IsNullOrWhiteSpace(errNo), $"stderr: {errNo}");
        if (codeWith == 0 && codeNo == 0)
        {
            using var docWith = JsonDocument.Parse(jsonWith);
            using var docNo = JsonDocument.Parse(jsonNo);
            var hitsWith = docWith.RootElement.EnumerateArray().ToList();
            var hitsNo = docNo.RootElement.EnumerateArray().ToList();
            if (hitsWith.Count > 0 && hitsNo.Count > 0)
            {
                // At least one hit should have non-null evidence when flag present, and all evidences should be null (or absent) when flag absent.
                bool anyEvidenceWith = hitsWith.Any(h => h.TryGetProperty("evidence", out var ev) && ev.ValueKind != JsonValueKind.Null && ev.EnumerateObject().Any());
                bool anyEvidenceWithout = hitsNo.Any(h => h.TryGetProperty("evidence", out var ev) && ev.ValueKind != JsonValueKind.Null && ev.EnumerateObject().Any());
                Assert.True(anyEvidenceWith, "Expected at least one evidence object when --evidence used");
                Assert.False(anyEvidenceWithout, "Did not expect evidence objects when --evidence omitted");
            }
        }
    }
}
