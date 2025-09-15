namespace AppLocate.Core.Abstractions {
    /// <summary>
    /// Lightweight registry for discovery sources to allow composable addition/removal without modifying orchestrator logic.
    /// Implementations should be thread-safe for concurrent enumeration.
    /// </summary>
    public interface ISourceRegistry {
        /// <summary>Returns the ordered collection of registered sources.</summary>
        IReadOnlyList<ISource> GetSources();
    }
}
