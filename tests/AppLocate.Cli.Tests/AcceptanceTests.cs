using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace AppLocate.Cli.Tests;

public class AcceptanceTests
{
    private static (string file, bool directExe) LocateCli()
    {
        var asmPath = typeof(AppLocate.Cli.Program).Assembly.Location;
        var exeCandidate = Path.ChangeExtension(asmPath, ".exe");
        if (File.Exists(exeCandidate)) return (exeCandidate, true);
        return (asmPath, false);
    }

    private static (int exitCode, string stdout, string stderr) RunWithEnv(string[] args, params (string key,string value)[] env)
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
        foreach (var (k,v) in env) psi.Environment[k] = v;
        var p = Process.Start(psi)!;
        string so = p.StandardOutput.ReadToEnd();
        string se = p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return (p.ExitCode, so, se);
    }

    private static string CreateDummyExe(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        // Write a minimal PE stub? Not needed; existence check only. Create empty file with .exe extension.
        if (!File.Exists(path)) File.WriteAllBytes(path, new byte[]{0});
        return path;
    }

    [Fact]
    public void VscodeScenario_ExeAndConfig()
    {
        // Arrange synthetic environment
        var root = Path.Combine(Path.GetTempPath(), "applocate_accept_vscode");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        var localAppData = Path.Combine(root, "Local");
        var roaming = Path.Combine(root, "Roaming");
        var progDir = Path.Combine(localAppData, "Programs", "Microsoft VS Code");
        var exe = CreateDummyExe(progDir, "Code.exe");
        var userSettingsDir = Path.Combine(roaming, "Code", "User");
        Directory.CreateDirectory(userSettingsDir);
        File.WriteAllText(Path.Combine(userSettingsDir, "settings.json"), "{}" );

        var pathEnv = progDir; // keep minimal PATH to speed up PathSearchSource

        // Act
        var (code, stdout, stderr) = RunWithEnv(new[]{"vscode","--json","--limit","10"},
            ("LOCALAPPDATA", localAppData),
            ("APPDATA", roaming),
            ("PATH", pathEnv));

        // Assert basic success (allow 0 or 1 if ranking filters, but expect 0)
        Assert.Equal(0, code);
        Assert.True(string.IsNullOrWhiteSpace(stderr), $"Unexpected stderr: {stderr}");
        var doc = JsonDocument.Parse(stdout);
        var hits = doc.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(hits);
        bool hasExe = hits.Any(h => h.GetProperty("type").GetString() == "exe" && h.GetProperty("path").GetString()!.EndsWith("Code.exe", StringComparison.OrdinalIgnoreCase));
        bool hasConfig = hits.Any(h => h.GetProperty("type").GetString() == "config" && h.GetProperty("path").GetString()!.EndsWith("settings.json", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasExe, "Expected exe hit for VSCode synthetic environment");
        Assert.True(hasConfig, "Expected config hit (settings.json) for VSCode synthetic environment");
    }

    [Fact]
    public void PortableAppScenario_InstallDirAndExe()
    {
        var root = Path.Combine(Path.GetTempPath(), "applocate_accept_portable");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        var portableDir = Path.Combine(root, "Tools", "FooApp");
        var exe = CreateDummyExe(portableDir, "FooApp.exe");
        var pathEnv = portableDir; // Aid PATH search

        var (code, stdout, stderr) = RunWithEnv(new[]{"fooapp","--json","--limit","10"}, ("PATH", pathEnv));
        Assert.Equal(0, code);
        Assert.True(string.IsNullOrWhiteSpace(stderr), $"Unexpected stderr: {stderr}");
        var doc = JsonDocument.Parse(stdout);
        var hits = doc.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(hits);
        bool hasExe = hits.Any(h => h.GetProperty("type").GetString() == "exe" && h.GetProperty("path").GetString()!.EndsWith("FooApp.exe", StringComparison.OrdinalIgnoreCase));
        bool hasInstall = hits.Any(h => h.GetProperty("type").GetString() == "install_dir" && h.GetProperty("path").GetString()!.Equals(portableDir, StringComparison.OrdinalIgnoreCase));
        Assert.True(hasExe, "Expected exe hit for portable app");
        Assert.True(hasInstall, "Expected install_dir hit for portable app");
    }
}
