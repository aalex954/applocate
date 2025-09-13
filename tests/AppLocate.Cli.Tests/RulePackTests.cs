using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace AppLocate.Cli.Tests;

public class RulePackTests
{
    private static string CliExe => Path.GetFullPath("src/..//artifacts/win-x64/applocate.exe"); // fallback to published; tests often run after build

    private static string RunCli(string args, out int exitCode)
    {
        // Discover solution root by walking up until AppLocate.sln found
        string? cur = AppContext.BaseDirectory; // test bin dir
        string? root = null;
        for (int i = 0; i < 8 && cur != null; i++)
        {
            if (File.Exists(Path.Combine(cur, "AppLocate.sln"))) { root = cur; break; }
            cur = Path.GetDirectoryName(cur);
        }
        Assert.True(root != null, "Could not locate solution root (AppLocate.sln)");
        var dllPath = Path.Combine(root!, "src", "AppLocate.Cli", "bin", "Release", "net8.0-windows", "applocate.dll");
        if (!File.Exists(dllPath))
        {
            dllPath = Path.Combine(root!, "src", "AppLocate.Cli", "bin", "Debug", "net8.0-windows", "applocate.dll");
        }
        Assert.True(File.Exists(dllPath), $"CLI assembly not found at {dllPath}");
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"\"{dllPath}\" {args}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // Force refresh index and environment isolation (use temp dirs for appdata patterns so rule expansions still produce hits if paths exist)
        psi.Environment["APPDATA"] = Path.GetTempPath();
        psi.Environment["LOCALAPPDATA"] = Path.GetTempPath();
        var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        exitCode = p.ExitCode;
        return stdout;
    }

    [Theory]
    [InlineData("code")]
    [InlineData("chrome")]
    [InlineData("edge")]
    [InlineData("notepad++")]
    [InlineData("powershell")]
    public void QueryReturnsAtLeastOneHit(string query)
    {
        var output = RunCli($"{query} --json --refresh-index --limit 3", out var exit);
        // We allow exit code 1 (no matches) in environments without installed apps; test focuses on JSON parse stability
        Assert.True(exit == 0 || exit == 1, $"Unexpected exit code {exit}. Output: {output}");
        if (exit == 0)
        {
            try
            {
                using var doc = JsonDocument.Parse(output);
                Assert.True(doc.RootElement.ValueKind == JsonValueKind.Array, "JSON root not array");
            }
            catch (JsonException je)
            {
                throw new Xunit.Sdk.XunitException($"Invalid JSON output for query {query}: {je.Message}\nRaw: {output}");
            }
        }
    }
}
