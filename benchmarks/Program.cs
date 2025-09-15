using System.Diagnostics;
using System.Text;

// Simple benchmark harness (not using BenchmarkDotNet to keep deps minimal).
// Measures cold vs warm search timings and thread scaling.

class Scenario
{
    public string Name { get; init; } = string.Empty;
    public string Args { get; init; } = string.Empty; // args excluding query sentinel
}

class BenchResult
{
    public string Scenario { get; init; } = string.Empty;
    public int Iteration { get; init; }
    public double Ms { get; init; }
    public int ExitCode { get; init; }
    public long StdoutBytes { get; init; }
}

static class CliRunner
{
    public static BenchResult Run(string scenario, string exe, string args, int iter)
    {
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // Provide temp isolated env (avoid cached index when measuring cold vs warm explicitly)
        psi.Environment["APPDATA"] = Path.GetTempPath();
        psi.Environment["LOCALAPPDATA"] = Path.GetTempPath();
        var p = Process.Start(psi)!;
        var outStr = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        sw.Stop();
        return new BenchResult { Scenario = scenario, Iteration = iter, Ms = sw.Elapsed.TotalMilliseconds, ExitCode = p.ExitCode, StdoutBytes = Encoding.UTF8.GetByteCount(outStr) };
    }
}

class Program
{
    static int Main(string[] args)
    {
        var cliDllRelease = Path.Combine(FindRoot(), "src","AppLocate.Cli","bin","Release","net8.0-windows","applocate.dll");
        var cliDllDebug = Path.Combine(FindRoot(), "src","AppLocate.Cli","bin","Debug","net8.0-windows","applocate.dll");
        var cli = File.Exists(cliDllRelease) ? cliDllRelease : cliDllDebug;
        if(!File.Exists(cli))
        {
            Console.Error.WriteLine("CLI dll not found. Build solution first.");
            return 2;
        }

        var scenarios = new []
        {
            new Scenario { Name = "cold_vscode_threads1", Args = "\"" + cli + "\" code --json --threads 1" },
            new Scenario { Name = "cold_vscode_threadsMax", Args = "\"" + cli + "\" code --json" },
            new Scenario { Name = "warm_vscode", Args = "\"" + cli + "\" code --json" },
            new Scenario { Name = "cold_chrome", Args = "\"" + cli + "\" chrome --json" },
        };

        var results = new List<BenchResult>();
        foreach (var sc in scenarios)
        {
            int iterations = sc.Name.StartsWith("warm") ? 5 : 3;
            for(int i=1;i<=iterations;i++)
            {
                var r = CliRunner.Run(sc.Name, "dotnet", sc.Args, i);
                results.Add(r);
                Console.WriteLine($"{r.Scenario},iter={r.Iteration},ms={r.Ms:F2},exit={r.ExitCode},bytes={r.StdoutBytes}");
                Thread.Sleep(50); // minimal spacing
            }
        }

        // Simple aggregate summary
        var grouped = results.GroupBy(r => r.Scenario);
        Console.WriteLine();
        Console.WriteLine("SUMMARY (avg ms):");
        foreach (var g in grouped)
        {
            Console.WriteLine($"{g.Key}: {g.Average(x=>x.Ms):F2} ms avg over {g.Count()} runs");
        }
        return 0;
    }

    static string FindRoot()
    {
        string? cur = AppContext.BaseDirectory;
        for(int i=0;i<8 && cur!=null;i++)
        {
            if(File.Exists(Path.Combine(cur,"AppLocate.sln"))) return cur;
            cur = Path.GetDirectoryName(cur);
        }
        return AppContext.BaseDirectory;
    }
}
