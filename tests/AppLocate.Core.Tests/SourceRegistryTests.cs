using System;
using System.Linq;
using System.Runtime.CompilerServices; // for EnumeratorCancellation attribute
using AppLocate.Core.Abstractions;
using AppLocate.Core.Sources;
using Xunit;

namespace AppLocate.Core.Tests;

public sealed class SourceRegistryTests
{
    private sealed class DummySource : ISource
    {
        public string Name { get; }
        public DummySource(string name) => Name = name;
        public async IAsyncEnumerable<AppLocate.Core.Models.AppHit> QueryAsync(string query, SourceOptions options, [EnumeratorCancellation] System.Threading.CancellationToken ct)
        {
            await System.Threading.Tasks.Task.Yield();
            yield break;
        }
    }

    [Fact]
    public void Builder_AddsInOrder()
    {
        var reg = new SourceRegistryBuilder()
            .Add(new DummySource("A"))
            .Add(new DummySource("B"))
            .Add(new DummySource("C"))
            .Build();
        var names = reg.GetSources().Select(s => s.Name).ToArray();
        Assert.Equal(new[] { "A", "B", "C" }, names);
    }

    [Fact]
    public void Builder_AddOrReplace_ReplacesInPlace()
    {
        var reg = new SourceRegistryBuilder()
            .Add(new DummySource("A"))
            .Add(new DummySource("B"))
            .AddOrReplace(new DummySource("B")) // replace B in place
            .Add(new DummySource("C"))
            .Build();
        var names = reg.GetSources().Select(s => s.Name).ToArray();
        Assert.Equal(new[] { "A", "B", "C" }, names);
    }

    [Fact]
    public void Builder_Remove_DropsSource()
    {
        var builder = new SourceRegistryBuilder()
            .Add(new DummySource("A"))
            .Add(new DummySource("B"))
            .Add(new DummySource("C"));
        builder.Remove("B");
        var reg = builder.Build();
        Assert.Equal(new[] { "A", "C" }, reg.GetSources().Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Builder_InsertBefore_InsertsAtCorrectIndex()
    {
        var reg = new SourceRegistryBuilder()
            .Add(new DummySource("A"))
            .Add(new DummySource("C"))
            .InsertBefore("C", new DummySource("B"))
            .Build();
        Assert.Equal(new[] { "A", "B", "C" }, reg.GetSources().Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Builder_Move_Reorders()
    {
        var reg = new SourceRegistryBuilder()
            .Add(new DummySource("A"))
            .Add(new DummySource("B"))
            .Add(new DummySource("C"))
            .Move("C", 0)
            .Build();
        Assert.Equal(new[] { "C", "A", "B" }, reg.GetSources().Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Builder_Duplicate_Add_Throws()
    {
        var b = new SourceRegistryBuilder();
        b.Add(new DummySource("A"));
        Assert.Throws<InvalidOperationException>(() => b.Add(new DummySource("A")));
    }

    [Fact]
    public void Builder_AfterBuild_ThrowsOnMutation()
    {
        var b = new SourceRegistryBuilder();
        b.Add(new DummySource("A"));
        var reg = b.Build();
        Assert.NotNull(reg);
        Assert.Throws<InvalidOperationException>(() => b.Add(new DummySource("B")));
    }
}
