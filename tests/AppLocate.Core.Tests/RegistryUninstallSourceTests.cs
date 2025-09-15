using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;
using AppLocate.Core.Sources;
using Xunit;

namespace AppLocate.Core.Tests {
    public class RegistryUninstallSourceTests {
        [Fact]
        public async Task ReturnsHitsForSyntheticHkcuEntry() {
            // Arrange: create a temporary HKCU uninstall key (safe – per-user) and point to a temp directory + exe.
            var appId = "AppLocateTestApp_" + Guid.NewGuid().ToString("N");
            using var tempDir = new TempDir();
            var exePath = Path.Combine(tempDir.Path, "TestApp.exe");
            File.WriteAllBytes(exePath, [0]); // placeholder file

            var keyPath = $"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{appId}";
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath)!) {
                k.SetValue("DisplayName", "Test App Locate");
                k.SetValue("InstallLocation", tempDir.Path);
                k.SetValue("DisplayIcon", exePath);
                k.SetValue("DisplayVersion", "1.2.3");
            }

            try {
                var src = new RegistryUninstallSource();
                // We want both user (HKCU) and machine disabled: MachineOnly=false ensures HKCU roots included.
                var opts = new SourceOptions(UserOnly: false, MachineOnly: false, Timeout: TimeSpan.FromSeconds(5), Strict: false, IncludeEvidence: true);
                var hits = new List<AppHit>();
                // Query uses a substring contained in DisplayName ("Test App Locate")
                await foreach (var h in src.QueryAsync("test app", opts, CancellationToken.None)) {
                    hits.Add(h);
                }

                // Assert
                Assert.NotEmpty(hits);
                Assert.Contains(hits, h => h.Type == HitType.InstallDir && h.Path == tempDir.Path);
                Assert.Contains(hits, h => h.Type == HitType.Exe && h.Path == exePath);
            }
            finally {
                // Cleanup the synthetic key
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            }
        }

        private sealed class TempDir : IDisposable {
            public string Path { get; }
            public TempDir() {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "applocate_regtest_" + Guid.NewGuid().ToString("N"));
                _ = Directory.CreateDirectory(Path);
            }
            public void Dispose() {
                try { Directory.Delete(Path, recursive: true); } catch { }
            }
        }
    }
}
