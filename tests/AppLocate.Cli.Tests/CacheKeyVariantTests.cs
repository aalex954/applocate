using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace AppLocate.Cli.Tests;

public class CacheKeyVariantTests
{
    [Fact]
    public async Task StrictAndNonStrict_CreateDistinctRecords()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "applocate-cache-variant-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var idxPath = Path.Combine(tempDir, "index.json");
        try
        {
            // Warm cache non-strict
            var code1 = await Program.RunAsync(new[]{"code","--index-path", idxPath, "--json"});
            Assert.True(code1 == 0 || code1 == 1);
            // Warm cache strict (should create a second record with composite key)
            var code2 = await Program.RunAsync(new[]{"code","--index-path", idxPath, "--json", "--strict"});
            Assert.True(code2 == 0 || code2 == 1);

            var json = await File.ReadAllTextAsync(idxPath);
            using var doc = JsonDocument.Parse(json);
            var records = doc.RootElement.GetProperty("Records");
            // Expect at least 2 distinct records containing 'code' but different composite key segments (strict flag s0 vs s1)
            var queries = records.EnumerateArray().Select(e => e.GetProperty("Query").GetString() ?? "").ToList();
            Assert.Contains(queries, q => q.Contains("|s0|"));
            Assert.Contains(queries, q => q.Contains("|s1|"));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive:true); } catch { }
        }
    }
}
