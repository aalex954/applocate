using System.Runtime.CompilerServices; // for EnumeratorCancellation attribute
using AppLocate.Core.Abstractions;
using AppLocate.Core.Sources;
using Xunit;

namespace AppLocate.Core.Tests {
    public sealed class SourceRegistryTests {
        private sealed class DummySource(string name) : ISource {
            public string Name { get; } = name;

            public async IAsyncEnumerable<Models.AppHit> QueryAsync(string query, SourceOptions options, [EnumeratorCancellation] CancellationToken ct) {
                await Task.Yield();
                yield break;
            }
        }

        [Fact]
        public void Builder_AddsInOrder() {
            var reg = new SourceRegistryBuilder()
                .Add(new DummySource("A"))
                .Add(new DummySource("B"))
                .Add(new DummySource("C"))
                .Build();
            var names = reg.GetSources().Select(s => s.Name).ToArray();
            string[] expected1 = ["A", "B", "C"]; // avoid span overload ambiguity
            Assert.Equal(expected1, names);
        }

        [Fact]
        public void Builder_AddOrReplace_ReplacesInPlace() {
            var reg = new SourceRegistryBuilder()
                .Add(new DummySource("A"))
                .Add(new DummySource("B"))
                .AddOrReplace(new DummySource("B")) // replace B in place
                .Add(new DummySource("C"))
                .Build();
            var names = reg.GetSources().Select(s => s.Name).ToArray();
            string[] expected2 = ["A", "B", "C"]; // avoid span overload ambiguity
            Assert.Equal(expected2, names);
        }

        [Fact]
        public void Builder_Remove_DropsSource() {
            var builder = new SourceRegistryBuilder()
                .Add(new DummySource("A"))
                .Add(new DummySource("B"))
                .Add(new DummySource("C"));
            _ = builder.Remove("B");
            var reg = builder.Build();
            string[] expected3 = ["A", "C"]; // avoid span overload ambiguity
            var actual3 = reg.GetSources().Select(s => s.Name).ToArray();
            Assert.Equal(expected3, actual3);
        }

        [Fact]
        public void Builder_InsertBefore_InsertsAtCorrectIndex() {
            var reg = new SourceRegistryBuilder()
                .Add(new DummySource("A"))
                .Add(new DummySource("C"))
                .InsertBefore("C", new DummySource("B"))
                .Build();
            string[] expected4 = ["A", "B", "C"]; // avoid span overload ambiguity
            var actual4 = reg.GetSources().Select(s => s.Name).ToArray();
            Assert.Equal(expected4, actual4);
        }

        [Fact]
        public void Builder_Move_Reorders() {
            var reg = new SourceRegistryBuilder()
                .Add(new DummySource("A"))
                .Add(new DummySource("B"))
                .Add(new DummySource("C"))
                .Move("C", 0)
                .Build();
            string[] expected5 = ["C", "A", "B"]; // avoid span overload ambiguity
            var actual5 = reg.GetSources().Select(s => s.Name).ToArray();
            Assert.Equal(expected5, actual5);
        }

        [Fact]
        public void Builder_Duplicate_Add_Throws() {
            var b = new SourceRegistryBuilder();
            _ = b.Add(new DummySource("A"));
            _ = Assert.Throws<InvalidOperationException>(() => b.Add(new DummySource("A")));
        }

        [Fact]
        public void Builder_AfterBuild_ThrowsOnMutation() {
            var b = new SourceRegistryBuilder();
            _ = b.Add(new DummySource("A"));
            var reg = b.Build();
            Assert.NotNull(reg);
            _ = Assert.Throws<InvalidOperationException>(() => b.Add(new DummySource("B")));
        }
    }
}
