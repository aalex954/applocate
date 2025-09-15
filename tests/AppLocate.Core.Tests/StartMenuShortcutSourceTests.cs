using System.Runtime.InteropServices;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;
using AppLocate.Core.Sources;
using Xunit;

namespace AppLocate.Core.Tests {
    public class StartMenuShortcutSourceTests {
        [Fact]
        public async Task ResolvesShortcutToExeAndInstallDir() {
            // Arrange synthetic Start Menu structure under a temp AppData
            using var layout = new TempLayout();
            // APPDATA root is layout.Root; StartMenu path = %APPDATA%\Microsoft\Windows\Start Menu\Programs
            var startMenuPrograms = Path.Combine(layout.Root, "Microsoft", "Windows", "Start Menu", "Programs");
            _ = Directory.CreateDirectory(startMenuPrograms);
            var appDir = Path.Combine(startMenuPrograms, "TestApp");
            _ = Directory.CreateDirectory(appDir);
            var exePath = Path.Combine(appDir, "TestApp.exe");
            File.WriteAllBytes(exePath, [0]);
            var lnkPath = Path.Combine(appDir, "TestApp.lnk");
            CreateShellLink(exePath, lnkPath, "Test App");

            // Point %AppData% used by source to our synthetic tree
            var originalAppData = Environment.GetEnvironmentVariable("APPDATA");
            Environment.SetEnvironmentVariable("APPDATA", layout.Root);
            try {
                var src = new StartMenuShortcutSource();
                var opts = new SourceOptions(UserOnly: false, MachineOnly: false, Timeout: TimeSpan.FromSeconds(5), Strict: false, IncludeEvidence: true);
                var hits = new List<AppHit>();
                await foreach (var h in src.QueryAsync("test app", opts, CancellationToken.None)) {
                    hits.Add(h);
                }

                Assert.Contains(hits, h => h.Type == HitType.Exe && h.Path.Equals(exePath, StringComparison.OrdinalIgnoreCase));
                Assert.Contains(hits, h => h.Type == HitType.InstallDir && h.Path.Equals(appDir, StringComparison.OrdinalIgnoreCase));
            }
            finally {
                Environment.SetEnvironmentVariable("APPDATA", originalAppData);
            }
        }

        private static void CreateShellLink(string targetPath, string linkPath, string description) {
            var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell unavailable");
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(linkPath);
            sc.TargetPath = targetPath;
            sc.Description = description;
            sc.WorkingDirectory = Path.GetDirectoryName(targetPath);
            sc.Save();
            Marshal.FinalReleaseComObject(sc);
            Marshal.FinalReleaseComObject(shell);
        }

        private sealed class TempLayout : IDisposable {
            public string Root { get; }
            public TempLayout() {
                Root = Path.Combine(Path.GetTempPath(), "applocate_shortcut_" + Guid.NewGuid().ToString("N"));
                _ = Directory.CreateDirectory(Root);
            }
            public void Dispose() {
                try { Directory.Delete(Root, true); } catch { }
            }
        }
    }
}
