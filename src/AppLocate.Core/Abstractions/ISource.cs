using AppLocate.Core.Models;

namespace AppLocate.Core.Abstractions {
    /// <summary>Options controlling source execution.</summary>
    public sealed record SourceOptions(bool UserOnly, bool MachineOnly, TimeSpan Timeout, bool Strict, bool IncludeEvidence, string? OriginalQuery = null);

    /// <summary>Contract for all data sources that can produce <see cref="AppHit"/> values.</summary>
    public interface ISource {
        /// <summary>
        /// Asynchronously queries this source for application hits that relate to the provided <paramref name="query"/>.
        /// Implementations should be non-throwing (swallow per-item errors) and respect cancellation promptly.
        /// Returned <see cref="AppHit"/> values should be raw (unscored) – ranking occurs in a separate layer.
        /// </summary>
        /// <param name="query">Normalized (lowercase) query string the user supplied; implementations should NOT re-normalize extensively.</param>
        /// <param name="options">Execution options controlling scope filtering, timeout, strict matching and evidence inclusion.</param>
        /// <param name="ct">Cancellation token to abort enumeration early.</param>
        /// <returns>An asynchronous stream of <see cref="AppHit"/> objects produced by this source.</returns>
        IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, CancellationToken ct);

        /// <summary>
        /// Stable source identifier used for provenance/evidence aggregation. Prefer <c>nameof(Type)</c> of the implementation.
        /// </summary>
        string Name { get; }
    }
}
