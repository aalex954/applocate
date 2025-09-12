using System;
using System.Collections.Generic;
using System.Threading;
using AppLocate.Core.Models;

namespace AppLocate.Core.Abstractions;

/// <summary>Options controlling source execution.</summary>
public sealed record SourceOptions(bool UserOnly, bool MachineOnly, TimeSpan Timeout, bool Strict, bool IncludeEvidence);

/// <summary>Contract for all data sources that can produce <see cref="AppHit"/> values.</summary>
public interface ISource
{
    IAsyncEnumerable<AppHit> QueryAsync(string query, SourceOptions options, CancellationToken ct);
    string Name { get; }
}
