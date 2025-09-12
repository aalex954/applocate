using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AppLocate.Core.Rules;

/// <summary>Placeholder rules engine. Will parse YAML and produce config/data paths.</summary>
internal sealed class RulesEngine
{
    public Task<IReadOnlyList<ResolvedRule>> LoadAsync(string file, CancellationToken ct)
    {
        if (!File.Exists(file)) return Task.FromResult<IReadOnlyList<ResolvedRule>>(Array.Empty<ResolvedRule>());
        // TODO: parse YAML (apps.default.yaml) once implemented.
        return Task.FromResult<IReadOnlyList<ResolvedRule>>(Array.Empty<ResolvedRule>());
    }
}

internal sealed record ResolvedRule(string MatchPattern, string[] Config, string[] Data);
