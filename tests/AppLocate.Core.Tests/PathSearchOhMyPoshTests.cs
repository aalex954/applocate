using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;
using AppLocate.Core.Sources;
using Xunit;

namespace AppLocate.Core.Tests;

public class PathSearchOhMyPoshTests
{

    [Fact]
    public async Task OhMyPosh_AliasForms_FindExe()
    {
        // Only run if the standard install exists on host; otherwise skip silently.
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrEmpty(pf86)) return; // implicit skip
        var candidate = Path.Combine(pf86, "oh-my-posh", "bin", "oh-my-posh.exe");
        if (!File.Exists(candidate)) return; // implicit skip when not installed

        var src = new PathSearchSource();
        var queries = new[] { "oh my posh", "oh-my-posh", "ohmyposh" };
        var opts = new SourceOptions(UserOnly: false, MachineOnly: false, Timeout: TimeSpan.FromSeconds(2), Strict: false, IncludeEvidence: true);
        int hits = 0;
        foreach (var q in queries)
        {
            var results = src.QueryAsync(q, opts, CancellationToken.None);
            bool found = false;
            await foreach (var hit in results)
            {
                if (hit.Type == HitType.Exe && string.Equals(hit.Path, candidate, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
            }
            if (found) hits++;
        }
        Assert.True(hits > 0, $"Expected at least one variant of query to locate oh-my-posh at {candidate}");
    }
}
