using AppLocate.Core.Models;
using AppLocate.Core.Ranking;
using Xunit;

namespace AppLocate.Core.Tests;

public class RankingNoiseFilteringTests
{
    [Fact]
    public void CollapsedAlias_OhMyPosh_SpacedVsCollapsed_CloseScores()
    {
        var exe = new AppHit(HitType.Exe, Scope.User, "C:/Users/x/AppData/Local/Programs/ohmyposh/ohmyposh.exe", null, PackageType.Portable, new[] { "Heuristic" }, 0, null);
        var spacedScore = Ranker.Score("oh my posh", exe);
        var collapsedScore = Ranker.Score("ohmyposh", exe);
        Assert.True(spacedScore > 0.35, $"Expected spaced alias to score; got {spacedScore}");
        Assert.True(System.Math.Abs(spacedScore - collapsedScore) < 0.20, $"Scores diverged too much: spaced={spacedScore} collapsed={collapsedScore}");
    }

    [Fact]
    public void Uninstaller_Penalized()
    {
        var uninstallExe = new AppHit(HitType.Exe, Scope.User, "C:/Users/x/App/unins000.exe", null, PackageType.EXE, new[] { "Heuristic" }, 0, null);
        var normalExe = new AppHit(HitType.Exe, Scope.User, "C:/Users/x/App/app.exe", null, PackageType.EXE, new[] { "Heuristic" }, 0, null);
        var s1 = Ranker.Score("app", normalExe);
        var s2 = Ranker.Score("app", uninstallExe);
        Assert.True(s1 > s2 + 0.15, $"Uninstaller not sufficiently penalized: normal={s1} uninstall={s2}");
    }

    [Fact]
    public void TempWingetDir_Penalized()
    {
        var tempDir = new AppHit(HitType.InstallDir, Scope.User, "C:/Users/x/AppData/Local/Temp/WinGet/App.1.2.3", null, PackageType.Winget, new[] { "Heuristic" }, 0, null);
        var mainDir = new AppHit(HitType.InstallDir, Scope.User, "C:/Users/x/AppData/Local/Programs/App", null, PackageType.Winget, new[] { "Heuristic" }, 0, null);
        var q = "app";
        var sTemp = Ranker.Score(q, tempDir);
        var sMain = Ranker.Score(q, mainDir);
        Assert.True(sMain > sTemp + 0.10, $"Temp dir not penalized enough: main={sMain} temp={sTemp}");
    }

    [Fact]
    public void CrossAppFlCloudPlugins_Suppressed()
    {
        var pluginPath = new AppHit(HitType.Exe, Scope.User, "C:/ProgramData/FL Cloud Plugins/flcloud.userRoaming/Telegram Desktop/Telegram.exe", null, PackageType.Unknown, new[] { "Heuristic" }, 0, null);
        var legitTelegram = new AppHit(HitType.Exe, Scope.User, "C:/Users/x/AppData/Roaming/Telegram Desktop/Telegram.exe", null, PackageType.Unknown, new[] { "Heuristic" }, 0, null);
        var sLegit = Ranker.Score("telegram", legitTelegram);
        var sPlugin = Ranker.Score("telegram", pluginPath);
        Assert.True(sLegit > sPlugin + 0.15, $"Cross-app plugin path not sufficiently suppressed: legit={sLegit} plugin={sPlugin}");
    }
}
