using System.Diagnostics;
using System.Text.Json;
using VerifyTests;
using VerifyXunit;
using Xunit;

namespace AppLocate.Cli.Tests.Snapshots;

public class CliSnapshotTests
{
    private static (string file, bool directExe) LocateCli()
    {
        // Use referenced assembly path (ProjectReference ensures build)
        var asmPath = typeof(AppLocate.Cli.Program).Assembly.Location; // applocate.dll
        var exeCandidate = Path.ChangeExtension(asmPath, ".exe");
        if (File.Exists(exeCandidate)) return (exeCandidate, true);
        return (asmPath, false); // run via dotnet
    }

    private static async Task<VerifyResult> RunAndVerifyAsync(string query, Dictionary<string,string>? env = null, params string[] extraArgs)
    {
        var args = new List<string>();
        args.Add(query);
        args.AddRange(extraArgs);
        var (cliPath, directExe) = LocateCli();
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (directExe)
        {
            psi.FileName = cliPath;
            foreach (var a in args) psi.ArgumentList.Add(a);
        }
        else
        {
            psi.FileName = "dotnet";
            psi.ArgumentList.Add(cliPath);
            foreach (var a in args) psi.ArgumentList.Add(a);
        }
        if (env != null)
        {
            foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
        }
        var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        p.WaitForExit(15000);
        var info = new
        {
            ExitCode = p.ExitCode,
            StdOut = TryFormatJson(stdout),
            StdErr = stderr.Trim()
        };
        return await Verifier.Verify(info).UseDirectory("CliSnapshots");
    }

    private static object TryFormatJson(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("["))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                // Project to stable subset: Type, Scope, PackageType (omit paths/confidence variability)
                string EnumToString(JsonElement e) => e.ValueKind switch
                {
                    JsonValueKind.String => e.GetString()!,
                    JsonValueKind.Number => e.GetRawText(), // numeric enum underlying value
                    _ => e.ToString()
                };
                var stable = doc.RootElement.EnumerateArray()
                    .Select(el => new
                    {
                        type = EnumToString(el.GetProperty("type")),
                        scope = EnumToString(el.GetProperty("scope")),
                        package_type = el.TryGetProperty("package_type", out var pt) ? EnumToString(pt) : null
                    }).ToList();
                return stable;
            }
            catch { }
        }
        // Attempt to parse text output format lines like: [0.23] Exe C:\Path\to\something.exe
    var lines = trimmed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var parsed = new List<object>();
        foreach (var line in lines)
        {
            var ln = line.Trim().TrimEnd(',');
            if (!ln.StartsWith("[")) continue;
            var closeIdx = ln.IndexOf(']');
            if (closeIdx <= 1) continue;
            // After confidence token, next token is the type label (Exe/InstallDir/Config/Data)
            var remainder = ln.Substring(closeIdx + 1).Trim();
            var firstSpace = remainder.IndexOf(' ');
            if (firstSpace < 1) continue;
            var typeToken = remainder.Substring(0, firstSpace);
            parsed.Add(new { type = typeToken });
        }
        if (parsed.Count > 0) return parsed; // stable projection for text output
        return lines.Length == 1 ? (object)lines[0] : lines;
    }

    private static Dictionary<string,string> CreateVscodeFixture()
    {
        // Synthetic VSCode per-user layout matching acceptance test approach.
        var root = Path.Combine(Path.GetTempPath(), "applocate_snapshot_vscode");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        var local = Path.Combine(root, "Local");
        var roaming = Path.Combine(root, "Roaming");
        var progDir = Path.Combine(local, "Programs", "Microsoft VS Code");
        Directory.CreateDirectory(progDir);
        var exePath = Path.Combine(progDir, "Code.exe");
        if (!File.Exists(exePath)) File.WriteAllBytes(exePath, new byte[]{0});
        var settingsDir = Path.Combine(roaming, "Code", "User");
        Directory.CreateDirectory(settingsDir);
        File.WriteAllText(Path.Combine(settingsDir, "settings.json"), "{}");
        // Restrict PATH to only program dir to make discovery deterministic.
        return new Dictionary<string,string>
        {
            ["LOCALAPPDATA"] = local,
            ["APPDATA"] = roaming,
            ["PATH"] = progDir
        };
    }

    [Fact]
    public async Task Query_Code_Json()
    {
        var env = CreateVscodeFixture();
        await RunAndVerifyAsync("code", env, "--json", "--limit", "5", "--refresh-index");
    }

    [Fact]
    public async Task Query_Chrome_Json()
    {
    // Temporarily disabled: environment dependent until acceptance fixtures prepared
    await Task.CompletedTask;
    }

    [Fact]
    public async Task Query_Code_Strict_Text()
    {
        var env = CreateVscodeFixture();
        await RunAndVerifyAsync("code", env, "--strict", "--limit", "3", "--refresh-index");
    }
}
