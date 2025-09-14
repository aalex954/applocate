using System.Collections.Generic;
using AppLocate.Core.Models;
using AppLocate.Core.Ranking;
using Xunit;

namespace AppLocate.Core.Tests;

/// <summary>
/// Phase 2 ranking refinement guard rails. These tests assert relative ordering for
/// new nuances (span compactness > noisy separation, early multi-source saturation,
/// and incremental fuzzy micro-boost vs unrelated).
/// They are written BEFORE Ranker phase 2 implementation changes to enforce intent.
/// If they fail after implementation, weights may need tuning not broad relaxation.
/// </summary>
public class RankingPhase2Tests
{
    private static AppHit Make(string path, HitType type = HitType.Exe, Dictionary<string,string>? evidence = null, string[]? sources = null)
        => new(type, Scope.User, path, null, PackageType.EXE, sources ?? new[]{"Test"}, 0, evidence);

    [Fact]
    public void ContiguousSpanBeatsSeparatedNoise()
    {
        var tight = Make("C:/apps/googlechrome.exe");
        var noisy = Make("C:/apps/google_x_helper_util_chrome.exe");
        var q = "google chrome";
        var sTight = Ranker.Score(q, tight);
        var sNoisy = Ranker.Score(q, noisy);
        // Tight should win or at worst tie within tiny epsilon (<0.01)
        Assert.True(sTight + 0.01 >= sNoisy, $"Span compactness regression: tight={sTight} noisy={sNoisy}");
    }

    [Fact]
    public void EarlyMultiSourceGainOutweighsLateGain()
    {
        var one = Make("C:/apps/app.exe", sources: new[]{"A"});
        var three = Make("C:/apps/app.exe", sources: new[]{"A","B","C"});
        var six = Make("C:/apps/app.exe", sources: new[]{"A","B","C","D","E","F"});
        var q = "app";
        var s1 = Ranker.Score(q, one);
        var s3 = Ranker.Score(q, three);
        var s6 = Ranker.Score(q, six);
        var gain13 = s3 - s1;
        var gain36 = s6 - s3;
        Assert.True(gain13 > 0);
        Assert.True(gain36 > 0);
        Assert.True(gain36 < gain13, $"Diminishing returns regression: early={gain13} late={gain36}");
    }

    [Fact]
    public void FuzzyMicroBoostImprovesOverUnrelated()
    {
        var fuzzy = Make("C:/apps/googlachrome.exe"); // small edit distance
        var unrelated = Make("C:/apps/someother.exe");
        var q = "google chrome";
        var sFuzzy = Ranker.Score(q, fuzzy);
        var sUnrelated = Ranker.Score(q, unrelated);
        Assert.True(sFuzzy > sUnrelated, $"Fuzzy micro-boost missing: fuzzy={sFuzzy} unrelated={sUnrelated}");
    }
}
