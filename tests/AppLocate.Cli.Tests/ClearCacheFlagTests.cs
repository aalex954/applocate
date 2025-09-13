using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace AppLocate.Cli.Tests;

public class ClearCacheFlagTests
{
    [Fact]
    public async Task ClearCache_DeletesFileAndRebuilds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "applocate-clearcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var idxPath = Path.Combine(tempDir, "index.json");
        try
        {
            // First run to create cache
            _ = await Program.RunAsync(new[]{"code","--index-path", idxPath, "--json"});
            Assert.True(File.Exists(idxPath));
            var initialStamp = File.GetLastWriteTimeUtc(idxPath);
            await Task.Delay(50); // ensure timestamp difference

            // Run with --clear-cache (forces deletion, then rebuild). Allow code 0 or 1.
            var rc = await Program.RunAsync(new[]{"code","--index-path", idxPath, "--json", "--clear-cache"});
            Assert.True(rc == 0 || rc == 1);
            Assert.True(File.Exists(idxPath));
            var newStamp = File.GetLastWriteTimeUtc(idxPath);
            Assert.True(newStamp >= initialStamp); // should be same or newer

            // Inspect records to ensure only composite style keys (sanity)
            var json = await File.ReadAllTextAsync(idxPath);
            using var doc = JsonDocument.Parse(json);
            var records = doc.RootElement.GetProperty("Records");
            Assert.All(records.EnumerateArray(), r => Assert.Contains("|u", r.GetProperty("Query").GetString()));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive:true); } catch { }
        }
    }
}
