using AppLocate.Core.Abstractions;

namespace AppLocate.Core.Sources {
    /// <summary>
    /// Simple in-memory implementation of <see cref="ISourceRegistry"/>. Construction order defines enumeration order.
    /// Future: could load plugins / external rule pack defined sources.
    /// </summary>
    /// <remarks>
    /// Initializes a new <see cref="SourceRegistry"/> with a fixed ordered sequence of <see cref="ISource"/> instances.
    /// Enumeration order is preserved; earlier sources are queried earlier (subject to parallelism at the caller).
    /// </remarks>
    /// <param name="sources">Concrete source implementations to register. Must not be null.</param>
    public sealed class SourceRegistry(IEnumerable<ISource> sources) : ISourceRegistry {
        private readonly List<ISource> _sources = [.. sources];

        /// <summary>
        /// Returns the registered sources in their configured order.
        /// </summary>
        /// <returns>Read-only list view of sources.</returns>
        public IReadOnlyList<ISource> GetSources() => _sources;
    }
}
