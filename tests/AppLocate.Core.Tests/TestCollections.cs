using Xunit;

namespace AppLocate.Core.Tests {
    /// <summary>
    /// Collection definition for tests that modify environment variables.
    /// Tests in this collection run serially to prevent race conditions in CI/local environments.
    /// </summary>
    [CollectionDefinition("EnvironmentVariableTests", DisableParallelization = true)]
    public class EnvironmentVariableTestsDefinition { }
}
