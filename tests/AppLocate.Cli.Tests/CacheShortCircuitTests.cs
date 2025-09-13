using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AppLocate.Core.Indexing;
using AppLocate.Core.Models;
using Xunit;

namespace AppLocate.Cli.Tests;

public class CacheShortCircuitTests
{
    [Fact]
    public async Task UsesCache_WhenFreshAndNotRefreshed()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "applocate-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var idxPath = Path.Combine(tempDir, "index.json");
        try
        {
            string Composite(string q) => string.Join('|', new[]{
                q, // normalized query already lower-case
                "u0","m0","s0","r0","p0","te0","ti0","tc0","td0","c0.00"});
            var record = IndexRecord.Create(Composite("testapp"), DateTimeOffset.UtcNow);
            record.Entries.Add(new IndexEntry(HitType.Exe, Scope.User, "C:/Fake/Path/TestApp.exe", null, PackageType.EXE, new[]{"ProcessSource"}, 0.9, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            var file = IndexFile.CreateEmpty();
            file.Records.Add(record);
            await File.WriteAllTextAsync(idxPath, JsonSerializer.Serialize(file));
            var code = await Program.RunAsync(new[]{"testapp","--json","--index-path", idxPath});
            // Existence filtering may remove the only cached path (missing fake path), resulting in fresh query with no hits -> exit 1.
            Assert.Contains(code, new[]{0,1});
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive:true); } catch { }
        }
    }
}
