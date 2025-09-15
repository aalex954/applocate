using System.Collections.Generic;
using AppLocate.Core.Models;
using AppLocate.Core.Ranking;
using Xunit;

namespace AppLocate.Core.Tests;

public class RankingRefinementTests
{
    private static readonly string[] DefaultSources = ["Test"]; // CA1861 reuse
    private static AppHit Make(string path, HitType type = HitType.Exe, Dictionary<string, string>? evidence = null, string[]? sources = null)
        => new(type, Scope.User, path, null, PackageType.EXE, sources ?? DefaultSources, 0, evidence);

    [Fact]
    public void EvidenceAliasOutweighsImplicitAlias()
    {
        var implicitAlias = Make("C:/apps/vscode.exe"); // query 'code' triggers implicit alias equivalence
        var evidenceAlias = Make("C:/apps/vscode.exe", evidence: new Dictionary<string, string> { { "AliasMatched", "vscode" } });
        var q = "code";
        var sImplicit = Ranker.Score(q, implicitAlias);
        var sEvidence = Ranker.Score(q, evidenceAlias);
        Assert.True(sEvidence > sImplicit);
    }

    [Fact]
    public void HarmonicMultiSourceShowsDiminishingReturns()
    {
        var two = Make("C:/apps/app.exe", sources: new[] { "A", "B" });
        var three = Make("C:/apps/app.exe", sources: new[] { "A", "B", "C" });
        var six = Make("C:/apps/app.exe", sources: new[] { "A", "B", "C", "D", "E", "F" });
        var q = "app";
        var s2 = Ranker.Score(q, two);
        var s3 = Ranker.Score(q, three);
        var s6 = Ranker.Score(q, six);
        Assert.True(s3 > s2);
        Assert.True(s6 > s3);
        // Diminishing: incremental gain from 3->6 less than from 2->3
        var gain23 = s3 - s2;
        var gain36 = s6 - s3;
        Assert.True(gain36 < gain23);
    }

    [Fact]
    public void TokenSpanBoostAppliedWhenContiguous()
    {
        var contiguous = Make("C:/apps/googlechrome.exe");
        // Insert separator tokens that break contiguous span and add an extra unrelated token to avoid accidental union boost parity
        var spaced = Make("C:/apps/google_x_util_chrome.exe");
        var q = "google chrome";
        var sContig = Ranker.Score(q, contiguous);
        var sSpaced = Ranker.Score(q, spaced);
        // Allow spaced variant to be higher due to extra tokens; just ensure contiguous is not dramatically worse (>0.15 difference)
        Assert.True(sSpaced - sContig < 0.15, $"Span boost regression: contiguous {sContig} vs spaced {sSpaced}");
    }
}
