using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AppLocate.Core.Indexing;
using AppLocate.Core.Models;
using Xunit;

namespace AppLocate.Cli.Tests;

public class CachePruneTests
{
    [Fact]
    public async Task LegacyRecordRemoved_OnPrune()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "applocate-prune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var idxPath = Path.Combine(tempDir, "index.json");
        try
        {
            var file = IndexFile.CreateEmpty();
            // Legacy simple key
            var legacy = IndexRecord.Create("legacyapp", DateTimeOffset.UtcNow.AddDays(-2));
            legacy.Entries.Add(new IndexEntry(HitType.Exe, Scope.User, "C:/Fake/legacy.exe", null, PackageType.EXE, new[]{"HeuristicFsSource"}, 0.5, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-2)));
            file.Records.Add(legacy);
            // Composite key (should be kept)
            var composite = IndexRecord.Create("legacyapp|u0|m0|s0|r0|p0|te0|ti0|tc0|td0|c0.00", DateTimeOffset.UtcNow);
            composite.Entries.Add(new IndexEntry(HitType.Exe, Scope.User, "C:/Fake/new.exe", null, PackageType.EXE, new[]{"HeuristicFsSource"}, 0.9, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            file.Records.Add(composite);
            await File.WriteAllTextAsync(idxPath, JsonSerializer.Serialize(file));

            // Trigger load + prune
            var exit = await Program.RunAsync(new[]{"legacyapp","--index-path", idxPath, "--json", "--verbose"});
            Assert.True(exit == 0 || exit == 1);

            var json = await File.ReadAllTextAsync(idxPath);
            using var doc = JsonDocument.Parse(json);
            var records = doc.RootElement.GetProperty("Records").EnumerateArray().Select(e => e.GetProperty("Query").GetString()).ToList();
            Assert.Contains("legacyapp|u0|m0|s0|r0|p0|te0|ti0|tc0|td0|c0.00", records);
            Assert.DoesNotContain("legacyapp", records);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
