using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace AppLocate.Cli.Tests;

public class IndexingTests
{
    [Fact]
    public async Task IndexFileCreated_OnQuery()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "applocate-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var idxPath = Path.Combine(tempDir, "index.json");
        try
        {
            var code = await Program.RunAsync(new[]{"nonexistentapp12345","--index-path", idxPath, "--json", "--limit","1"});
            Assert.True(code == 0 || code == 1);
            Assert.True(File.Exists(idxPath));
            var json = await File.ReadAllTextAsync(idxPath);
            Assert.Contains("\"version\"", json);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive:true); } catch { }
        }
    }
}
