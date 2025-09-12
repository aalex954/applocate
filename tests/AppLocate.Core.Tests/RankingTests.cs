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

    [Fact]
    public void ShortcutAndProcessSynergyBeatsIndividual()
    {
        var baseHit = Make("C:/apps/app.exe");
        var shortcut = Make("C:/apps/app.exe", evidence: new Dictionary<string,string>{{"Shortcut","x"}});
        var process = Make("C:/apps/app.exe", evidence: new Dictionary<string,string>{{"ProcessId","123"}});
        var synergy = Make("C:/apps/app.exe", evidence: new Dictionary<string,string>{{"Shortcut","x"},{"ProcessId","123"}});
        var q = "app";
        var sBase = Ranker.Score(q, baseHit);
        var sShortcut = Ranker.Score(q, shortcut);
        var sProcess = Ranker.Score(q, process);
        var sSynergy = Ranker.Score(q, synergy);
        Assert.True(sShortcut > sBase && sProcess > sBase);
        Assert.True(sSynergy > sShortcut && sSynergy > sProcess);
    }

    [Fact]
    public void AliasBoostImprovesScore()
    {
        var noAlias = Make("C:/apps/code.exe");
        var alias = Make("C:/apps/code.exe", evidence: new Dictionary<string,string>{{"AliasMatched","vscode"}});
        var q = "code";
        var s1 = Ranker.Score(q, noAlias);
        var s2 = Ranker.Score(q, alias);
        Assert.True(s2 > s1);
    }

    [Fact]
    public void BrokenShortcutPenaltyLowersScore()
    {
        var normal = Make("C:/apps/tool.exe");
        var penalized = Make("C:/apps/tool.exe", evidence: new Dictionary<string,string>{{"BrokenShortcut","true"}});
        var q = "tool";
        var s1 = Ranker.Score(q, normal);
        var s2 = Ranker.Score(q, penalized);
        Assert.True(s2 < s1);
    }

    [Fact]
    public void MultiSourceSaturation()
    {
        var four = Make("C:/apps/app.exe", sources: new[]{"A","B","C","D"});
        var eight = Make("C:/apps/app.exe", sources: new[]{"A","B","C","D","E","F","G","H"});
        var q = "app";
        var s4 = Ranker.Score(q, four);
        var s8 = Ranker.Score(q, eight);
        Assert.True(s8 >= s4);
        Assert.True((s8 - s4) < 0.10); // diminishing returns
    }
}
