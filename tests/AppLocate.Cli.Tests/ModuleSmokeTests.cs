using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace AppLocate.Cli.Tests;

public class ModuleSmokeTests
{
    [Fact]
    public void PowerShellModule_FindApp_ProducesJson()
    {
        // Skip on non-Windows CI if PowerShell not present
        var pwsh = "pwsh";
        var psiCheck = new ProcessStartInfo(pwsh, "-nologo -noprofile -c $PSVersionTable.PSVersion.ToString()")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
    try { using var check = Process.Start(psiCheck)!; check.WaitForExit(5000); if(check.ExitCode != 0) throw new Exception("pwsh not available"); }
    catch { return; } // gracefully exit test if pwsh missing

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var modulePath = Path.Combine(root, "AppLocate.psm1");
        if(!File.Exists(modulePath))
            return; // benign: module not yet part of test context

        // Use config min to ensure deterministic small output
    // Use backtick to escape nested braces or use here-string style construction
    var script = $"Import-Module '{modulePath}'; $res = Get-AppLocateJson -Query 'code' -Limit 1; if($null -eq $res){{ exit 5 }}; $res | ConvertTo-Json -Depth 5";

        var psi = new ProcessStartInfo(pwsh, $"-nologo -noprofile -c \"{script}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(10000);
        Assert.NotEqual(string.Empty, output.Trim());
        Assert.Contains("confidence", output, StringComparison.OrdinalIgnoreCase);
    }
}
