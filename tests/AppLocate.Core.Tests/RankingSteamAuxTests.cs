using AppLocate.Core.Models;using AppLocate.Core.Ranking;using Xunit;

namespace AppLocate.Core.Tests;

public class RankingSteamAuxTests
{
    [Fact]
    public void SteamPrimary_OutranksAuxiliary()
    {
        var primary = new AppHit(HitType.Exe, Scope.Machine, "C:/Program Files (x86)/Steam/Steam.exe", null, PackageType.EXE, new[]{"Registry","Shortcut"}, 0, null);
        var webhelper = new AppHit(HitType.Exe, Scope.Machine, "C:/Program Files (x86)/Steam/bin/cef/cef.win7x64/steamwebhelper.exe", null, PackageType.EXE, new[]{"FileSystem"}, 0, null);
        var service = new AppHit(HitType.Exe, Scope.Machine, "C:/Program Files (x86)/Steam/bin/steamservice.exe", null, PackageType.EXE, new[]{"FileSystem"}, 0, null);
        var err = new AppHit(HitType.Exe, Scope.Machine, "C:/Program Files (x86)/Steam/steamerrorreporter.exe", null, PackageType.EXE, new[]{"FileSystem"}, 0, null);
        var q = "steam";
        var sPrimary = Ranker.Score(q, primary);
        var sWeb = Ranker.Score(q, webhelper);
        var sSvc = Ranker.Score(q, service);
        var sErr = Ranker.Score(q, err);
        Assert.True(sPrimary > sWeb + 0.15 && sPrimary > sSvc + 0.15 && sPrimary > sErr + 0.15, $"Primary not sufficiently ahead: primary={sPrimary} web={sWeb} svc={sSvc} err={sErr}");
    }
}
