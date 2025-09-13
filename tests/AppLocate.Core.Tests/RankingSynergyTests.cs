using System.Collections.Generic;
using AppLocate.Core.Models;
using AppLocate.Core.Ranking;
using Xunit;

namespace AppLocate.Core.Tests;

public class RankingSynergyTests
{
    private static AppHit Make(Dictionary<string,string>? evidence) => new(
        HitType.Exe,
        Scope.User,
        "C:/apps/testapp/test.exe",
        null,
        PackageType.EXE,
        new[]{"Test"},
        0,
        evidence
    );

    [Fact]
    public void ShortcutPlusProcessBeatsSingleSignal()
    {
        var q = "testapp";
        var shortcutOnly = Make(new Dictionary<string,string>{{"Shortcut","foo.lnk"}});
        var processOnly = Make(new Dictionary<string,string>{{"ProcessId","1234"}});
        var both = Make(new Dictionary<string,string>{{"Shortcut","foo.lnk"},{"ProcessId","1234"}});

        var sShortcut = Ranker.Score(q, shortcutOnly);
        var sProcess = Ranker.Score(q, processOnly);
        var sBoth = Ranker.Score(q, both);

        Assert.True(sBoth > sShortcut);
        Assert.True(sBoth > sProcess);
    }
}
