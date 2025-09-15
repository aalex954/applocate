using System.IO;
using System.Threading;
using AppLocate.Core.Rules;
using Xunit;

namespace AppLocate.Core.Tests;

public class RulesEngineTests
{
    [Fact]
    public async System.Threading.Tasks.Task ParsesSampleYaml()
    {
        var yaml = "# sample\n- match:\n  anyOf: [\"Visual Studio Code\", \"Code.exe\", \"vscode\"]\n  config: [\"%APPDATA%/Code/User/settings.json\"]\n  data: [\"%APPDATA%/Code/*\"]";
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, yaml);
        var res = await RulesEngine.LoadAsync(tmp, CancellationToken.None);
        Assert.Single(res);
        Assert.Contains("vscode", res[0].MatchAnyOf);
        Assert.Single(res[0].Config);
        Assert.Single(res[0].Data);
    }
}
