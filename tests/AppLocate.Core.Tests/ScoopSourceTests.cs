using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;
using AppLocate.Core.Sources;
using Xunit;

namespace AppLocate.Core.Tests {
    /// <summary>
    /// Deterministic tests for ScoopSource using fake provider via APPLOCATE_SCOOP_FAKE env var.
    /// Tests in this collection run serially to avoid environment variable race conditions.
    /// </summary>
    [Collection("EnvironmentVariableTests")]
    public sealed class ScoopSourceTests {
        // Simpler fixture format matching what FakeScoopProvider expects
        private static readonly string FakeFixture = /*lang=json,strict*/ """
    {
        "roots": ["C:\\Users\\testuser\\scoop"],
        "apps": [
            {
                "name": "7zip",
                "root": "C:\\Users\\testuser\\scoop",
                "version": "24.08",
                "exes": ["7z.exe", "7zFM.exe"],
                "bin": ["7z.exe", "7zFM.exe"],
                "persist": false
            },
            {
                "name": "git",
                "root": "C:\\Users\\testuser\\scoop",
                "version": "2.45.2",
                "exes": ["git.exe"],
                "bin": ["git.exe"],
                "persist": true
            }
        ]
    }
    """;

        private static SourceOptions DefaultOptions => new(false, false, TimeSpan.FromSeconds(5), false, false);
        private static SourceOptions OptionsWithEvidence => new(false, false, TimeSpan.FromSeconds(5), false, true);
        private static SourceOptions UserOnlyOptions => new(true, false, TimeSpan.FromSeconds(5), false, false);
        private static SourceOptions MachineOnlyOptions => new(false, true, TimeSpan.FromSeconds(5), false, false);

        private static async Task<List<AppHit>> CollectHitsAsync(ScoopSource source, string query, SourceOptions options) {
            var hits = new List<AppHit>();
            await foreach (var hit in source.QueryAsync(query, options, CancellationToken.None)) {
                hits.Add(hit);
            }
            return hits;
        }

        [Fact]
        public async Task Query_7zip_Returns_InstallDir_And_Exes() {
            // Arrange - set env var before creating source
            Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", FakeFixture);
            try {
                var source = new ScoopSource();

                // Act
                var hits = await CollectHitsAsync(source, "7zip", DefaultOptions);

                // Assert
                Assert.NotEmpty(hits);

                var installDir = hits.FirstOrDefault(h => h.Type == HitType.InstallDir);
                Assert.NotNull(installDir);
                Assert.Equal(Scope.User, installDir.Scope);
                Assert.Contains("7zip", installDir.Path, StringComparison.OrdinalIgnoreCase);
                Assert.Equal("24.08", installDir.Version);
                Assert.Equal(PackageType.Scoop, installDir.PackageType);

                var exes = hits.Where(h => h.Type == HitType.Exe).ToList();
                Assert.Equal(2, exes.Count);
                Assert.Contains(exes, e => e.Path.EndsWith("7z.exe", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(exes, e => e.Path.EndsWith("7zFM.exe", StringComparison.OrdinalIgnoreCase));
            }
            finally {
                Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", null);
            }
        }

        [Fact]
        public async Task Query_Git_Returns_InstallDir_Exe_And_PersistData() {
            // Arrange
            Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", FakeFixture);
            try {
                var source = new ScoopSource();

                // Act
                var hits = await CollectHitsAsync(source, "git", DefaultOptions);

                // Assert
                Assert.NotEmpty(hits);

                var installDir = hits.FirstOrDefault(h => h.Type == HitType.InstallDir);
                Assert.NotNull(installDir);
                Assert.Equal("2.45.2", installDir.Version);

                var exe = hits.FirstOrDefault(h => h.Type == HitType.Exe);
                Assert.NotNull(exe);
                Assert.EndsWith("git.exe", exe.Path, StringComparison.OrdinalIgnoreCase);

                var dataHit = hits.FirstOrDefault(h => h.Type == HitType.Data);
                Assert.NotNull(dataHit);
                Assert.Contains("persist", dataHit.Path, StringComparison.OrdinalIgnoreCase);
            }
            finally {
                Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", null);
            }
        }

        [Fact]
        public async Task Query_NoMatch_Returns_Empty() {
            // Arrange
            Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", FakeFixture);
            try {
                var source = new ScoopSource();

                // Act
                var hits = await CollectHitsAsync(source, "nonexistent", DefaultOptions);

                // Assert
                Assert.Empty(hits);
            }
            finally {
                Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", null);
            }
        }

        [Fact]
        public async Task Query_UserOnly_Returns_UserScope() {
            // Arrange
            Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", FakeFixture);
            try {
                var source = new ScoopSource();

                // Act
                var hits = await CollectHitsAsync(source, "7zip", UserOnlyOptions);

                // Assert - all hits should be user scope (our fixture is user-only)
                Assert.NotEmpty(hits);
                Assert.All(hits, h => Assert.Equal(Scope.User, h.Scope));
            }
            finally {
                Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", null);
            }
        }

        [Fact]
        public async Task Query_MachineOnly_SkipsUserScope() {
            // Arrange - fixture only has user-scope apps
            Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", FakeFixture);
            try {
                var source = new ScoopSource();

                // Act
                var hits = await CollectHitsAsync(source, "7zip", MachineOnlyOptions);

                // Assert - should be empty since fixture only has user-scope
                Assert.Empty(hits);
            }
            finally {
                Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", null);
            }
        }

        [Fact]
        public async Task Query_WithEvidence_IncludesEvidenceData() {
            // Arrange
            Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", FakeFixture);
            try {
                var source = new ScoopSource();

                // Act
                var hits = await CollectHitsAsync(source, "7zip", OptionsWithEvidence);

                // Assert
                var installDir = hits.First(h => h.Type == HitType.InstallDir);
                Assert.NotNull(installDir.Evidence);
                Assert.Equal("7zip", installDir.Evidence["ScoopApp"]);
                Assert.Contains("scoop", installDir.Evidence["ScoopRoot"], StringComparison.OrdinalIgnoreCase);
            }
            finally {
                Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", null);
            }
        }

        [Fact]
        public async Task Query_PartialMatch_FindsApp() {
            // Arrange
            Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", FakeFixture);
            try {
                var source = new ScoopSource();

                // Act - "7z" should match "7zip"
                var hits = await CollectHitsAsync(source, "7z", DefaultOptions);

                // Assert
                Assert.NotEmpty(hits);
                Assert.Contains(hits, h => h.Path.Contains("7zip", StringComparison.OrdinalIgnoreCase));
            }
            finally {
                Environment.SetEnvironmentVariable("APPLOCATE_SCOOP_FAKE", null);
            }
        }
    }
}
