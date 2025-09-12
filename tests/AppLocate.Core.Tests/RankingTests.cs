using System.Collections.Generic;
using AppLocate.Core.Models;
using AppLocate.Core.Ranking;
using Xunit;

namespace AppLocate.Core.Tests;

public class RankingTests
{
    private static AppHit Make(string path, HitType type = HitType.Exe, Dictionary<string,string>? evidence = null, string[]? sources = null)
        => new(type, Scope.User, path, null, PackageType.EXE, sources ?? new[]{"Test"}, 0, evidence);

    [Fact]
    public void ExactFileNameBeatsSubstring()
    {
        var exact = Make("C:/tools/code.exe");
        var sub = Make("C:/tools/somecodehelper.exe");
        var q = "code";
        var sExact = Ranker.Score(q, exact);
        var sSub = Ranker.Score(q, sub);
        Assert.True(sExact > sSub);
    }

    [Fact]
    public void ShortcutAndProcessBoostIncreaseScore()
    {
        var baseHit = Make("C:/apps/app.exe");
        var boosted = Make("C:/apps/app.exe", evidence: new Dictionary<string,string>{{"Shortcut","x"},{"ProcessId","123"}});
        var q = "app";
        var sBase = Ranker.Score(q, baseHit);
        var sBoost = Ranker.Score(q, boosted);
        Assert.True(sBoost > sBase);
    }

    [Fact]
    public void MultiSourceAddsBoost()
    {
        var single = Make("C:/apps/tool.exe", sources: new[]{"A"});
        var multi = Make("C:/apps/tool.exe", sources: new[]{"A","B","C"});
        var q = "tool";
        var sSingle = Ranker.Score(q, single);
        var sMulti = Ranker.Score(q, multi);
        Assert.True(sMulti > sSingle);
    }
}
