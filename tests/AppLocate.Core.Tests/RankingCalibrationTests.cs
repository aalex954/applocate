using AppLocate.Core.Models;
using AppLocate.Core.Ranking;
using Xunit;

namespace AppLocate.Core.Tests {
    public class RankingCalibrationTests {
        private static readonly string[] DefaultSources = ["Test"]; // CA1861 reuse
        private static AppHit Make(string path, HitType type = HitType.Exe, Dictionary<string, string>? evidence = null, string[]? sources = null)
            => new(type, Scope.User, path, null, PackageType.EXE, sources ?? DefaultSources, 0, evidence);

        [Fact]
        public void AliasEquivalenceBoostsButLessThanExact() {
            var exact = Make("C:/tools/code.exe");
            var aliasTarget = Make("C:/tools/vscode.exe");
            var qExact = "code"; // exact for first, alias for second
            var sExact = Ranker.Score(qExact, exact);
            var sAlias = Ranker.Score(qExact, aliasTarget);
            Assert.True(sExact > sAlias); // alias < exact
            Assert.True(sAlias > 0);      // still positive
        }

        [Fact]
        public void FuzzyPartialTokensAddsIncrementalScore() {
            var partial = Make("C:/apps/googlechromeportable.exe");
            var qPartial = "google chrome"; // tokens: google, chrome
            var sPartial = Ranker.Score(qPartial, partial);
            var unrelated = Make("C:/apps/otherapp.exe");
            var sUnrelated = Ranker.Score(qPartial, unrelated);
            Assert.True(sPartial > sUnrelated);
        }

        [Fact]
        public void TempInstallerPenaltyReducesScore() {
            var normal = Make("C:/apps/app.exe");
            var temp = Make("C:/apps/Temp/app.exe"); // ensure casing variant still triggers penalty
            var q = "app";
            var sNormal = Ranker.Score(q, normal);
            var sTemp = Ranker.Score(q, temp);
            Assert.True(sNormal > sTemp);
        }

        [Fact]
        public void BrokenShortcutPenaltyStillKeepsNonZeroWhenOtherSignalsStrong() {
            var penalized = Make("C:/apps/code.exe", evidence: new Dictionary<string, string> { { "BrokenShortcut", "1" }, { "ProcessId", "123" } });
            var q = "code";
            var s = Ranker.Score(q, penalized);
            Assert.True(s > 0); // penalty doesn't zero out
        }
    }
}
