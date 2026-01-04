using Xunit;
using AppLocate.Core.Sources;
using AppLocate.Core.Abstractions;
using AppLocate.Core.Models;

namespace AppLocate.Core.Tests;

/// <summary>Tests for package manager sources (Scoop, Chocolatey, Winget).</summary>
public class PackageManagerSourceTests {
    [Fact]
    public void ScoopSource_HasCorrectName() {
        var source = new ScoopSource();
        Assert.Equal(nameof(ScoopSource), source.Name);
    }

    [Fact]
    public void ChocolateySource_HasCorrectName() {
        var source = new ChocolateySource();
        Assert.Equal(nameof(ChocolateySource), source.Name);
    }

    [Fact]
    public void WingetSource_HasCorrectName() {
        var source = new WingetSource();
        Assert.Equal(nameof(WingetSource), source.Name);
    }

    [Fact]
    public async Task ScoopSource_EmptyQuery_YieldsNothing() {
        var source = new ScoopSource();
        var options = new SourceOptions(false, false, TimeSpan.FromSeconds(5), false, false);
        
        var hits = new List<AppHit>();
        await foreach (var hit in source.QueryAsync("", options, CancellationToken.None)) {
            hits.Add(hit);
        }
        
        Assert.Empty(hits);
    }

    [Fact]
    public async Task ChocolateySource_EmptyQuery_YieldsNothing() {
        var source = new ChocolateySource();
        var options = new SourceOptions(false, false, TimeSpan.FromSeconds(5), false, false);
        
        var hits = new List<AppHit>();
        await foreach (var hit in source.QueryAsync("", options, CancellationToken.None)) {
            hits.Add(hit);
        }
        
        Assert.Empty(hits);
    }

    [Fact]
    public async Task WingetSource_EmptyQuery_YieldsNothing() {
        var source = new WingetSource();
        var options = new SourceOptions(false, false, TimeSpan.FromSeconds(5), false, false);
        
        var hits = new List<AppHit>();
        await foreach (var hit in source.QueryAsync("", options, CancellationToken.None)) {
            hits.Add(hit);
        }
        
        Assert.Empty(hits);
    }

    [Fact]
    public async Task ScoopSource_NoScoopInstalled_YieldsNothing() {
        // This test verifies graceful handling when Scoop is not installed
        // If Scoop IS installed, it will still work (just may return hits)
        var source = new ScoopSource();
        var options = new SourceOptions(false, false, TimeSpan.FromSeconds(5), false, false);
        
        // Use a query that won't match anything
        var hits = new List<AppHit>();
        await foreach (var hit in source.QueryAsync("__nonexistent_app_xyz123__", options, CancellationToken.None)) {
            hits.Add(hit);
        }
        
        Assert.Empty(hits);
    }

    [Fact]
    public async Task ChocolateySource_NoChocoInstalled_YieldsNothing() {
        // This test verifies graceful handling when Chocolatey is not installed
        var source = new ChocolateySource();
        var options = new SourceOptions(false, false, TimeSpan.FromSeconds(5), false, false);
        
        var hits = new List<AppHit>();
        await foreach (var hit in source.QueryAsync("__nonexistent_app_xyz123__", options, CancellationToken.None)) {
            hits.Add(hit);
        }
        
        Assert.Empty(hits);
    }

    [Fact]
    public async Task ChocolateySource_UserOnly_YieldsNothing() {
        // Chocolatey is always machine scope, so --user should yield nothing
        var source = new ChocolateySource();
        var options = new SourceOptions(UserOnly: true, MachineOnly: false, TimeSpan.FromSeconds(5), false, false);
        
        var hits = new List<AppHit>();
        await foreach (var hit in source.QueryAsync("test", options, CancellationToken.None)) {
            hits.Add(hit);
        }
        
        Assert.Empty(hits);
    }
}
