namespace AppLocate.Cli.Tests {
    public class CliOptionsTests {
        [Fact]
        public async Task JsonOption_ParsesAndReturnsSuccessOrNoHits() {
            // Will likely return exit code 1 (no hits) but should not error.
            var code = await Program.RunAsync(["vscode", "--json", "--limit", "1"]);
            Assert.True(code is 0 or 1);
        }

        [Fact]
        public async Task LimitOption_AllowsZeroOrPositive() {
            var code = await Program.RunAsync(["testapp", "--limit", "2"]);
            Assert.True(code is 0 or 1);
        }

        [Fact]
        public async Task ConfidenceMinRejectsInvalid() {
            var code = await Program.RunAsync(["foo", "--confidence-min", "2"]); // invalid >1
            Assert.Equal(2, code);
        }

        [Fact]
        public async Task MissingQueryReturns2() {
            var code = await Program.RunAsync(["--json"]);
            Assert.Equal(2, code);
        }
    }
}
