using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;
using AppLocate.Core.Sources;
using Xunit;

namespace AppLocate.Core.Tests;

/// <summary>
/// Deterministic tests for ChocolateySource using fake provider via APPLOCATE_CHOCO_FAKE env var.
/// </summary>
public sealed class ChocolateySourceTests {
    private static readonly string FakeFixture = """
    {
        "root": "C:\\ProgramData\\chocolatey",
        "directories": [
            "C:\\ProgramData\\chocolatey\\lib",
            "C:\\ProgramData\\chocolatey\\lib\\7zip",
            "C:\\ProgramData\\chocolatey\\lib\\7zip\\tools",
            "C:\\ProgramData\\chocolatey\\lib\\7zip\\.chocolatey",
            "C:\\ProgramData\\chocolatey\\lib\\git",
            "C:\\ProgramData\\chocolatey\\lib\\git\\tools",
            "C:\\ProgramData\\chocolatey\\bin"
        ],
        "directoryContents": {
            "C:\\ProgramData\\chocolatey\\lib": [
                "C:\\ProgramData\\chocolatey\\lib\\7zip",
                "C:\\ProgramData\\chocolatey\\lib\\git"
            ]
        },
        "filesByPattern": {
            "C:\\ProgramData\\chocolatey\\bin|*.exe": [
                "C:\\ProgramData\\chocolatey\\bin\\7z.exe",
                "C:\\ProgramData\\chocolatey\\bin\\git.exe"
            ],
            "C:\\ProgramData\\chocolatey\\lib\\7zip\\tools|*.exe": [
                "C:\\ProgramData\\chocolatey\\lib\\7zip\\tools\\7z.exe"
            ],
            "C:\\ProgramData\\chocolatey\\lib\\git\\tools|*.exe": [
                "C:\\ProgramData\\chocolatey\\lib\\git\\tools\\git.exe"
            ],
            "C:\\ProgramData\\chocolatey\\lib\\7zip\\.chocolatey|*.nuspec": []
        },
        "fileContents": {},
        "xmlContents": {
            "C:\\ProgramData\\chocolatey\\lib\\7zip\\7zip.nuspec": "<?xml version=\"1.0\"?><package xmlns=\"http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd\"><metadata><id>7zip</id><version>24.08</version><title>7-Zip</title></metadata></package>",
            "C:\\ProgramData\\chocolatey\\lib\\git\\git.nuspec": "<?xml version=\"1.0\"?><package xmlns=\"http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd\"><metadata><id>git</id><version>2.45.2</version><title>Git for Windows</title></metadata></package>"
        }
    }
    """;

    private static SourceOptions DefaultOptions => new(false, false, TimeSpan.FromSeconds(5), false, false);
    private static SourceOptions OptionsWithEvidence => new(false, false, TimeSpan.FromSeconds(5), false, true);
    private static SourceOptions UserOnlyOptions => new(true, false, TimeSpan.FromSeconds(5), false, false);
    private static SourceOptions MachineOnlyOptions => new(false, true, TimeSpan.FromSeconds(5), false, false);

    private static async Task<List<AppHit>> CollectHitsAsync(ChocolateySource source, string query, SourceOptions options) {
        var hits = new List<AppHit>();
        await foreach (var hit in source.QueryAsync(query, options, CancellationToken.None)) {
            hits.Add(hit);
        }
        return hits;
    }

    [Fact]
    public async Task Query_7zip_Returns_InstallDir_And_Exes() {
        // Arrange
        Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", FakeFixture);
        try {
            var source = new ChocolateySource();

            // Act
            var hits = await CollectHitsAsync(source, "7zip", DefaultOptions);

            // Assert
            Assert.NotEmpty(hits);

            var installDir = hits.FirstOrDefault(h => h.Type == HitType.InstallDir);
            Assert.NotNull(installDir);
            Assert.Equal(Scope.Machine, installDir.Scope); // Chocolatey is always machine scope
            Assert.Contains("7zip", installDir.Path, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("24.08", installDir.Version);
            Assert.Equal(PackageType.Chocolatey, installDir.PackageType);

            var exes = hits.Where(h => h.Type == HitType.Exe).ToList();
            Assert.NotEmpty(exes);
        }
        finally {
            Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", null);
        }
    }

    [Fact]
    public async Task Query_Git_Returns_InstallDir_And_Exe() {
        // Arrange
        Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", FakeFixture);
        try {
            var source = new ChocolateySource();

            // Act
            var hits = await CollectHitsAsync(source, "git", DefaultOptions);

            // Assert
            Assert.NotEmpty(hits);

            var installDir = hits.FirstOrDefault(h => h.Type == HitType.InstallDir);
            Assert.NotNull(installDir);
            Assert.Equal("2.45.2", installDir.Version);

            var exe = hits.FirstOrDefault(h => h.Type == HitType.Exe);
            Assert.NotNull(exe);
        }
        finally {
            Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", null);
        }
    }

    [Fact]
    public async Task Query_NoMatch_Returns_Empty() {
        // Arrange
        Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", FakeFixture);
        try {
            var source = new ChocolateySource();

            // Act
            var hits = await CollectHitsAsync(source, "nonexistent", DefaultOptions);

            // Assert
            Assert.Empty(hits);
        }
        finally {
            Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", null);
        }
    }

    [Fact]
    public async Task Query_UserOnly_Returns_Empty_BecauseChocoIsMachineScope() {
        // Arrange
        Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", FakeFixture);
        try {
            var source = new ChocolateySource();

            // Act
            var hits = await CollectHitsAsync(source, "7zip", UserOnlyOptions);

            // Assert - should be empty since Chocolatey is always machine-scope
            Assert.Empty(hits);
        }
        finally {
            Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", null);
        }
    }

    [Fact]
    public async Task Query_MachineOnly_ReturnsResults() {
        // Arrange
        Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", FakeFixture);
        try {
            var source = new ChocolateySource();

            // Act
            var hits = await CollectHitsAsync(source, "7zip", MachineOnlyOptions);

            // Assert - should have results since Chocolatey is machine-scope
            Assert.NotEmpty(hits);
        }
        finally {
            Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", null);
        }
    }

    [Fact]
    public async Task Query_WithEvidence_IncludesEvidenceData() {
        // Arrange
        Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", FakeFixture);
        try {
            var source = new ChocolateySource();

            // Act
            var hits = await CollectHitsAsync(source, "7zip", OptionsWithEvidence);

            // Assert
            var installDir = hits.First(h => h.Type == HitType.InstallDir);
            Assert.NotNull(installDir.Evidence);
            Assert.Equal("7zip", installDir.Evidence["ChocoPackage"]);
            Assert.Contains("chocolatey", installDir.Evidence["ChocoRoot"], StringComparison.OrdinalIgnoreCase);
            Assert.Equal("7-Zip", installDir.Evidence["Title"]);
        }
        finally {
            Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", null);
        }
    }

    [Fact]
    public async Task Query_PartialPackageName_FindsApp() {
        // Arrange
        Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", FakeFixture);
        try {
            var source = new ChocolateySource();

            // Act - "7z" should match package name "7zip"
            var hits = await CollectHitsAsync(source, "7z", DefaultOptions);

            // Assert
            Assert.NotEmpty(hits);
            Assert.Contains(hits, h => h.Path.Contains("7zip", StringComparison.OrdinalIgnoreCase));
        }
        finally {
            Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", null);
        }
    }

    [Fact]
    public async Task Query_AllHitsAreMachineScope() {
        // Arrange
        Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", FakeFixture);
        try {
            var source = new ChocolateySource();

            // Act
            var hits = await CollectHitsAsync(source, "git", DefaultOptions);

            // Assert - Chocolatey is always machine-scope
            Assert.NotEmpty(hits);
            Assert.All(hits, h => Assert.Equal(Scope.Machine, h.Scope));
        }
        finally {
            Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", null);
        }
    }

    [Fact]
    public async Task Query_ConfigHit_WhenChocolateyMetaDirExists() {
        // Arrange - fixture has .chocolatey dir for 7zip
        Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", FakeFixture);
        try {
            var source = new ChocolateySource();

            // Act
            var hits = await CollectHitsAsync(source, "7zip", DefaultOptions);

            // Assert
            var configHit = hits.FirstOrDefault(h => h.Type == HitType.Config);
            Assert.NotNull(configHit);
            Assert.Contains(".chocolatey", configHit.Path, StringComparison.OrdinalIgnoreCase);
        }
        finally {
            Environment.SetEnvironmentVariable("APPLOCATE_CHOCO_FAKE", null);
        }
    }
}
