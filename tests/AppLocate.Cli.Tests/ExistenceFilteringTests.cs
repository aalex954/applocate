using System;using System.IO;using System.Linq;using System.Text.Json;using System.Threading.Tasks;using Xunit;

namespace AppLocate.Cli.Tests;

public class ExistenceFilteringTests
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
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (direct) { psi.FileName = cli; foreach (var a in args) psi.ArgumentList.Add(a); }
        else { psi.FileName = "dotnet"; psi.ArgumentList.Add(cli); foreach (var a in args) psi.ArgumentList.Add(a); }
        var p = System.Diagnostics.Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEnd();
        var se = p.StandardError.ReadToEnd();
        p.WaitForExit(10000);
        return (p.ExitCode, so, se);
    }

    [Fact]
    public async Task CacheRemovesMissingPaths_OnReuse()
    {
        // Build a temporary index with a cached record referencing a missing path then query to trigger sanitization.
        var tempDir = Path.Combine(Path.GetTempPath(), "applocate-exist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var idxPath = Path.Combine(tempDir, "index.json");
        try
        {
            string composite = string.Join('|', new[]{"ghostapp","u0","m0","s0","r0","p0","te0","ti0","tc0","td0","c0.00"});
            var file = AppLocate.Core.Indexing.IndexFile.CreateEmpty();
            var rec = AppLocate.Core.Indexing.IndexRecord.Create(composite, DateTimeOffset.UtcNow);
            // Non-existent exe
            rec.Entries.Add(new AppLocate.Core.Indexing.IndexEntry(AppLocate.Core.Models.HitType.Exe, AppLocate.Core.Models.Scope.User, Path.Combine(tempDir, "DoesNotExist.exe"), null, AppLocate.Core.Models.PackageType.EXE, new[]{"Test"}, 0.9, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            file.Records.Add(rec);
            await File.WriteAllTextAsync(idxPath, JsonSerializer.Serialize(file));
            var (code1, stdout1, stderr1) = Run("ghostapp", "--index-path", idxPath, "--json", "--verbose");
            // Expect exit 1 because sanitized cache removes the only hit
            Assert.Equal(1, code1);
            Assert.Contains("cache stale: all paths missing", stderr1);
            // Subsequent run should now have empty cached record and exit 1 but without sanitization message (no entries to remove)
            var (code2, stdout2, stderr2) = Run("ghostapp", "--index-path", idxPath, "--json", "--verbose");
            Assert.Equal(1, code2);
            // Ensure the index file record has zero entries now
            var json = await File.ReadAllTextAsync(idxPath);
            using var doc = JsonDocument.Parse(json);
            var records = doc.RootElement.GetProperty("Records").EnumerateArray().ToList();
            var ghost = records.Single(r => r.GetProperty("Query").GetString()!.StartsWith("ghostapp|"));
            var entries = ghost.GetProperty("Entries").EnumerateArray().ToList();
            Assert.Empty(entries);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
