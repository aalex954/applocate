using System.Collections.Generic;
using AppLocate.Core.Abstractions;

namespace AppLocate.Core.Sources;

/// <summary>
/// Simple in-memory implementation of <see cref="ISourceRegistry"/>. Construction order defines enumeration order.
/// Future: could load plugins / external rule pack defined sources.
/// </summary>
public sealed class SourceRegistry : ISourceRegistry
{
    private readonly List<ISource> _sources;
    public SourceRegistry(IEnumerable<ISource> sources)
    {
        _sources = new List<ISource>(sources);
    }
    public IReadOnlyList<ISource> GetSources() => _sources;
}
