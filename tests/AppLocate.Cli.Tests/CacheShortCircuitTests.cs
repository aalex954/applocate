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
            var record = IndexRecord.Create("testapp", DateTimeOffset.UtcNow);
            record.Entries.Add(new IndexEntry(HitType.Exe, Scope.User, "C:/Fake/Path/TestApp.exe", null, PackageType.EXE, new[]{"ProcessSource"}, 0.9, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            var file = IndexFile.CreateEmpty();
            file.Records.Add(record);
            await File.WriteAllTextAsync(idxPath, JsonSerializer.Serialize(file));
            var code = await Program.RunAsync(new[]{"testapp","--json","--index-path", idxPath});
            Assert.Equal(0, code);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive:true); } catch { }
        }
    }
}
