using System.Diagnostics;using System.Text.Json;using Xunit;using System.IO;using System.Linq;using System.Threading.Tasks;

namespace AppLocate.Cli.Tests;

public class AllOptionTests
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
    public void AllOption_ReturnsMoreOrEqualHits()
    {
        // Use a query likely to produce more than one hit of same type in synthetic fixtures (e.g., code) via Start Menu + path + uninstall heuristics.
        // If environment only yields single hits, allow equality but assert no error.
        var (codeCollapsed, jsonCollapsed, errCollapsed) = Run("code", "--json", "--limit", "50", "--refresh-index");
        Assert.Contains(codeCollapsed, new[]{0,1});
        Assert.True(string.IsNullOrWhiteSpace(errCollapsed), $"stderr (collapsed): {errCollapsed}");
        var (codeAll, jsonAll, errAll) = Run("code", "--json", "--all", "--limit", "50", "--refresh-index");
        Assert.Contains(codeAll, new[]{0,1});
        Assert.True(string.IsNullOrWhiteSpace(errAll), $"stderr (all): {errAll}");
        if (codeCollapsed == 0 && codeAll == 0)
        {
            int Count(string json){ try { using var doc = JsonDocument.Parse(json); return doc.RootElement.GetArrayLength(); } catch { return 0; } }
            var collapsedCount = Count(jsonCollapsed);
            var allCount = Count(jsonAll);
            Assert.True(allCount >= collapsedCount, $"Expected allCount >= collapsedCount but {allCount} < {collapsedCount}. Collapsed JSON: {jsonCollapsed}\nAll JSON: {jsonAll}");
        }
    }
}
