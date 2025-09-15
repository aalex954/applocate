using System.Collections.Generic;
using AppLocate.Core.Models;
using AppLocate.Core.Ranking;
using Xunit;

namespace AppLocate.Core.Tests;

public class RankingPunctuationSplitTests
{
    private static readonly string[] DefaultSources = ["Test"]; // CA1861 reuse
    private static AppHit Make(string path, HitType type = HitType.Exe, string[]? sources = null)
        => new(type, Scope.User, path, null, PackageType.EXE, sources ?? DefaultSources, 0, null);

    [Fact]
    public void CompressedTokenMatchesHyphenated()
    {
        var hit = Make("C:/apps/oh-my-posh.exe");
        var score = Ranker.Score("ohmyposh", hit);
        Assert.True(score > 0.2, $"Expected score > 0.2, got {score}");
    }

    [Fact]
    public void WingetIdVariantMatchesExecutable()
    {
        var hit = Make("C:/apps/oh-my-posh.exe");
        var score = Ranker.Score("jandedobbeleer.ohmyposh", hit);
        Assert.True(score > 0.15, $"Expected score > 0.15, got {score}");
    }

    [Fact]
    public void PunctuationSplitImprovesCoverage()
    {
        var hit = Make("C:/apps/oh-my-posh.exe");
        var baseScore = Ranker.Score("posh", hit);
        var multiScore = Ranker.Score("oh my posh", hit);
        Assert.True(multiScore >= baseScore, $"Expected multi token score >= single token. base={baseScore} multi={multiScore}");
    }
}
