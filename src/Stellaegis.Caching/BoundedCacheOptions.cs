using System.Globalization;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Caching;

/// <summary>
/// Tuning options for a <see cref="BoundedCache"/>: the entry bound, the backstop TTL, and the
/// auto-sizing parameters used when <see cref="SizeLimit"/> is left unset.
/// </summary>
/// <remarks>
/// <para>
/// Correctness is expected to be carried by explicit invalidation (e.g. a change notification), not by
/// expiry — the TTL is only a backstop, so it can be long as long as the entry count stays bounded.
/// </para>
/// <para>
/// The options say what is wrong with them, <see cref="FindError"/>: a cache registered with dependency
/// injection is validated when the host starts, so that a bound that would remove no entry per compaction
/// fails the host rather than the first resolution of the cache.
/// </para>
/// </remarks>
public sealed class BoundedCacheOptions : IValidatableOptions
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

    /// <summary>
    /// Fraction of entries removed per overflow compaction pass, in the range (0, 1]. Combined with the
    /// effective bound, it must remove at least one entry: with the default 0.05, a bound of 20 or more.
    /// </summary>
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

    /// <summary>
    /// The first setting that keeps the cache from working, or <see langword="null"/>: a bound below 1, a
    /// percentage out of its range, a clamp upside down, an expiration below zero, or a bound and a
    /// compaction percentage that would remove no entry per compaction — a full cache would then refuse
    /// writes until its entries expire.
    /// </summary>
    public string? FindError()
    {
        if (SizeLimit is { } limit && limit < 1)
            return string.Create(CultureInfo.InvariantCulture, $"SizeLimit is {limit}: it must be positive, or left unset to derive the bound from memory.");
        if (double.IsNaN(CompactionPercentage) || CompactionPercentage is <= 0 or > 1)
            return string.Create(CultureInfo.InvariantCulture, $"CompactionPercentage is {CompactionPercentage}: it must be in (0, 1].");
        if (double.IsNaN(MemoryFraction) || MemoryFraction is <= 0 or >= 1)
            return string.Create(CultureInfo.InvariantCulture, $"MemoryFraction is {MemoryFraction}: it must be in (0, 1).");
        if (EstimatedEntryBytes < 1)
            return string.Create(CultureInfo.InvariantCulture, $"EstimatedEntryBytes is {EstimatedEntryBytes}: it must be positive.");
        if (MinEntries < 1 || MaxEntries < MinEntries)
            return string.Create(CultureInfo.InvariantCulture, $"MinEntries is {MinEntries} and MaxEntries {MaxEntries}: 1 <= MinEntries <= MaxEntries is required.");
        if (Expiration < TimeSpan.Zero)
            return string.Create(CultureInfo.InvariantCulture, $"Expiration is {Expiration}: it must be zero or positive.");

        // MemoryCache removes (int)(count × percentage) entries per compaction: below one, it removes
        // only expired entries, and a full cache stays full.
        var bound = BoundedCache.ResolveSizeLimit(this);
        if ((long)(bound * CompactionPercentage) < 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"A SizeLimit of {bound} with a CompactionPercentage of {CompactionPercentage} removes no entry per ")
                + "compaction. Raise one of them.";
        }
        return null;
    }
}
