using System.Threading.Tasks;
using Xunit;

namespace AppLocate.Cli.Tests;

public class CliExitCodeTests
{
    [Fact]
    public async Task Returns1WhenNoHits()
    {
    var code = await Program.RunAsync(new[] { "nonexistent-app-xyz" });
    Assert.Equal(1, code);
    }
}
