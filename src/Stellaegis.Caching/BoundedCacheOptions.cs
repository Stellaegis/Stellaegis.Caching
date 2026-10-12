namespace Stellaegis.Caching;

/// <summary>
/// Tuning options for a <see cref="BoundedCache"/>: the entry bound, the backstop TTL, and the
/// auto-sizing parameters used when <see cref="SizeLimit"/> is left unset.
/// </summary>
/// <remarks>
/// Correctness is expected to be carried by explicit invalidation (e.g. a change notification), not by
/// expiry — the TTL is only a backstop, so it can be long as long as the entry count stays bounded.
/// </remarks>
public sealed class BoundedCacheOptions
{
    /// <summary>
    /// The default configuration section holding one child section per cache, keyed by cache name:
    /// <c>Stellaegis:Caching:&lt;name&gt;</c>.
    /// </summary>
    public const string SectionName = "Stellaegis:Caching";

    /// <summary>
    /// Hard entry-count bound. When <see langword="null"/> (the default) the bound is derived at
    /// construction from available memory (see <see cref="MemoryFraction"/>,
    /// <see cref="EstimatedEntryBytes"/>, <see cref="MinEntries"/>, <see cref="MaxEntries"/>).
    /// A positive value overrides the derivation; each entry counts as size 1.
    /// </summary>
    public long? SizeLimit { get; init; }

    /// <summary>
    /// Backstop time-to-live applied to entries that do not specify their own TTL. Long by design.
    /// </summary>
    public TimeSpan Expiration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Fraction of entries removed per overflow compaction pass, in the range (0, 1).</summary>
    public double CompactionPercentage { get; init; } = 0.05;

    /// <summary>
    /// Fraction of total available memory the auto-derived bound may claim, in the range (0, 1).
    /// Ignored when <see cref="SizeLimit"/> is set explicitly.
    /// </summary>
    public double MemoryFraction { get; init; } = 0.05;

    /// <summary>
    /// Estimated cost in bytes of a single cached entry, used by the auto-derivation.
    /// Ignored when <see cref="SizeLimit"/> is set explicitly.
    /// </summary>
    public long EstimatedEntryBytes { get; init; } = 1024;

    /// <summary>Lower clamp for the auto-derived bound.</summary>
    public long MinEntries { get; init; } = 1024;

    /// <summary>Upper clamp for the auto-derived bound.</summary>
    public long MaxEntries { get; init; } = 1_000_000;
}
