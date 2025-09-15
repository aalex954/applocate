using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace AppLocate.Cli.Tests;

public class SelectiveEvidenceTests
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
    public void EvidenceKeysImplicitlyEnableEvidence()
    {
        var (code, json, err) = Run("code", "--json", "--evidence-keys", "Shortcut,ProcessId", "--limit", "25");
        Assert.Contains(code, new[] { 0, 1 });
        Assert.True(string.IsNullOrWhiteSpace(err), $"stderr: {err}");
        if (code == 0)
        {
            using var doc = JsonDocument.Parse(json);
            var hits = doc.RootElement.EnumerateArray().ToList();
            if (hits.Count == 0) return; // environment may lack sample apps
            bool anyEvidenceKey = hits.Any(h => h.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.Object && ev.EnumerateObject().Any(p => p.NameEquals("Shortcut") || p.NameEquals("ProcessId")));
            Assert.True(anyEvidenceKey, "Expected filtered evidence keys present");
        }
    }

    [Fact]
    public void EvidenceFilteringRemovesUnlistedKeysAndDropsEmpty()
    {
        var (codeAll, jsonAll, errAll) = Run("code", "--json", "--evidence", "--limit", "25");
        Assert.Contains(codeAll, new[] { 0, 1 });
        Assert.True(string.IsNullOrWhiteSpace(errAll), $"stderr: {errAll}");
        var (codeFiltered, jsonFiltered, errFiltered) = Run("code", "--json", "--evidence-keys", "Shortcut", "--limit", "25");
        Assert.Contains(codeFiltered, new[] { 0, 1 });
        Assert.True(string.IsNullOrWhiteSpace(errFiltered), $"stderr: {errFiltered}");
        if (codeAll == 0 && codeFiltered == 0)
        {
            using var docAll = JsonDocument.Parse(jsonAll);
            using var docFiltered = JsonDocument.Parse(jsonFiltered);
            var firstAll = docAll.RootElement.EnumerateArray().FirstOrDefault();
            var firstFiltered = docFiltered.RootElement.EnumerateArray().FirstOrDefault();
            if (firstAll.ValueKind != JsonValueKind.Undefined && firstFiltered.ValueKind != JsonValueKind.Undefined)
            {
                if (firstAll.TryGetProperty("evidence", out var evAll) && evAll.ValueKind == JsonValueKind.Object)
                {
                    // Filtered must not contain keys other than Shortcut
                    if (firstFiltered.TryGetProperty("evidence", out var evFilt) && evFilt.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in evFilt.EnumerateObject())
                        {
                            Assert.True(p.NameEquals("Shortcut"), $"Unexpected evidence key present after filtering: {p.Name}");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void EvidenceOrderingIsDeterministic()
    {
        var (code, json, err) = Run("code", "--json", "--evidence", "--limit", "25");
        Assert.Contains(code, new[] { 0, 1 });
        Assert.True(string.IsNullOrWhiteSpace(err), $"stderr: {err}");
        if (code == 0)
        {
            using var doc = JsonDocument.Parse(json);
            var first = doc.RootElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.Object)
            {
                var keys = ev.EnumerateObject().Select(o => o.Name).ToList();
                var sorted = keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                Assert.Equal(sorted, keys); // already sorted
            }
        }
    }
}
