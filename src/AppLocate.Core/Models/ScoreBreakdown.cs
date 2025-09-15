namespace AppLocate.Core.Models {
    /// <summary>
    /// Represents the individual scoring component contributions that sum to the final <see cref="AppHit.Confidence"/>.
    /// Only populated when the caller requests a score breakdown (e.g. CLI --score-breakdown).
    /// Values are already clamped where appropriate; <see cref="Total"/> should equal the confidence produced by
    /// the ranking engine after final adjustments (post penalties and caps).
    /// </summary>
    public sealed record ScoreBreakdown(
        double TokenCoverage,
        double CollapsedSubstring,
        double PartialTokenJaccard,
        double FilenameExactOrPartial,
        double AliasEquivalence,
        double DirAlias,
        double EvidenceBoosts,
        double EvidenceSynergy,
        double EvidencePenalties,
        double PathPenalties,
        double ContiguousSpan,
        double NoisePenalties,
        double MultiSource,
        double TypeBaseline,
        double FuzzyLevenshtein,
        double ExactMatchBonus,
        double UninstallPenalty,
        double SteamAuxPenalty,
        double PluginSuppression,
        double CacheArtifactPenalty,
        double PairingBoost,
        double GenericDirPenalty,
        double DirMinFloor,
        double OrphanProbeAdjustments,
        double VariantSiblingBoost,
        double Total,
        IReadOnlyDictionary<string, double>? RawSignals
    );
}
