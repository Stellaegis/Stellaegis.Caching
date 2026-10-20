using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Stellaegis.Caching;

/// <summary>
/// A size-bounded in-memory cache with a long backstop TTL, optional segment invalidation, and
/// built-in metrics. Wraps a dedicated <see cref="MemoryCache"/> so its bound is independent of the
/// shared application cache.
/// </summary>
/// <remarks>
/// <para>
/// The entry count never exceeds the bound. A write into a full cache is dropped and counted as an
/// eviction, and the underlying <see cref="MemoryCache"/> starts a background compaction that removes
/// <see cref="BoundedCacheOptions.CompactionPercentage"/> of the entries — expired entries first, then
/// least-recently-used. This is an approximate LRU, adequate for a backstop bound.
/// </para>
/// <para>
/// Freshness is not the cache's job: entries are invalidated on demand through the change tokens
/// passed to <see cref="Set{T}"/> (e.g. cancelled when a change notification arrives). The TTL only bounds the blast
/// radius of a missed invalidation, so it can be long once the size is bounded.
/// </para>
/// <para>
/// The cache knows nothing of compartments: a key is a string. An application with compartments puts the
/// space in the key, <c>$"{space.ToSegment()}:sku:{sku}"</c>, from the first day — a key without it is
/// shared between compartments the day the application takes a second one, without an error.
/// </para>
/// <para>
/// Metrics are emitted on the <c>Stellaegis.Caching</c> meter, one for the process, each measurement
/// tagged with <c>cache.name</c>: <c>stellaegis.cache.entries</c>, <c>stellaegis.cache.hits</c>,
/// <c>stellaegis.cache.misses</c>, <c>stellaegis.cache.evictions</c>. A disposed cache stops reporting.
/// </para>
/// </remarks>
public sealed class BoundedCache : IDisposable
{
    /// <summary>The meter name under which all bounded caches publish their instruments.</summary>
    public const string MeterName = "Stellaegis.Caching";

    // One meter and one set of instruments for the process, observing every live cache: a meter per cache
    // would publish as many homonymous instruments as there are caches, and keep observing a disposed one.
    private static readonly Meter Meter = new(MeterName);
    private static readonly ConcurrentDictionary<BoundedCache, byte> Live = new(ReferenceEqualityComparer.Instance);

    static BoundedCache()
    {
        Meter.CreateObservableGauge(
            "stellaegis.cache.entries", () => Observe(cache => cache.GetStatistics()?.CurrentEntryCount ?? 0), unit: "{entry}",
            description: "Number of entries currently held by the bounded cache.");
        Meter.CreateObservableCounter(
            "stellaegis.cache.hits", () => Observe(cache => cache.GetStatistics()?.TotalHits ?? 0), unit: "{hit}",
            description: "Total cache hits since the cache was created.");
        Meter.CreateObservableCounter(
            "stellaegis.cache.misses", () => Observe(cache => cache.GetStatistics()?.TotalMisses ?? 0), unit: "{miss}",
            description: "Total cache misses since the cache was created.");
        Meter.CreateObservableCounter(
            "stellaegis.cache.evictions", () => Observe(cache => cache.EvictionCount), unit: "{eviction}",
            description: "Total entries evicted for capacity, writes dropped by a full cache included, since the cache was created.");
    }

    private readonly MemoryCache _cache;
    private readonly BoundedCacheOptions _options;
    private readonly KeyValuePair<string, object?> _nameTag;
    private long _evictions;
    private volatile bool _disposed;

    /// <summary>Creates a bounded cache named <paramref name="name"/> from <paramref name="options"/>.</summary>
    /// <param name="name">Identifier used as the <c>cache.name</c> metric tag.</param>
    /// <param name="options">Size, TTL, and auto-sizing configuration.</param>
    /// <exception cref="ArgumentException">
    /// The options do not hold (<see cref="BoundedCacheOptions.FindError"/>): a bound and a
    /// <see cref="BoundedCacheOptions.CompactionPercentage"/> that would remove no entry per compaction, for instance.
    /// </exception>
    public BoundedCache(string name, BoundedCacheOptions options)
    {
        Name = Guard.NotWhiteSpace(name);
        _options = Guard.NotNull(options);
        if (options.FindError() is { } error)
            throw new ArgumentException($"Cache '{Name}': {error}", nameof(options));
        SizeLimit = ResolveSizeLimit(options);

        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = SizeLimit,
            CompactionPercentage = options.CompactionPercentage,
            TrackStatistics = true,
        });

        _nameTag = new KeyValuePair<string, object?>("cache.name", Name);
        Live[this] = 0;
    }

    /// <summary>The cache name, used as the <c>cache.name</c> metric tag.</summary>
    public string Name { get; }

    /// <summary>The effective entry bound in force (explicit or auto-derived).</summary>
    public long SizeLimit { get; }

    /// <summary>Number of entries evicted for capacity since construction, writes dropped by a full cache included.</summary>
    public long EvictionCount => Interlocked.Read(ref _evictions);

    /// <summary>Current underlying statistics (entry count, hits, misses), or <see langword="null"/>.</summary>
    public MemoryCacheStatistics? GetStatistics() => _cache.GetCurrentStatistics();

    /// <summary>
    /// Attempts to read a cached value. Increments hit/miss statistics as a side effect. Returns
    /// <see langword="false"/> when the key is absent. A value stored under the key that is not a
    /// <typeparamref name="T"/> is a key shared by two consumers, and fails rather than reads as absent.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value stored under <paramref name="key"/> is not a <typeparamref name="T"/>.</exception>
    public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        Guard.NotWhiteSpace(key);
        // A stored null reads as absent: there is nothing to hand out, and nothing to tell a type from.
        if (!_cache.TryGetValue(key, out var cached) || cached is null)
        {
            value = default;
            return false;
        }

        if (cached is T typed)
        {
            value = typed;
            return true;
        }

        throw new InvalidOperationException(
            $"Cache '{Name}': the key '{key}' holds a {cached.GetType().Name}, and a {typeof(T).Name} is read. "
            + "Two consumers share the key: give each its own.");
    }

    /// <summary>
    /// Stores a value under <paramref name="key"/> with size 1, the given invalidation tokens, and a TTL.
    /// </summary>
    /// <param name="key">The cache key. Must not be null, empty, or whitespace.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="invalidationTokens">
    /// Optional change tokens that evict the entry when signalled (e.g. segment invalidation).
    /// </param>
    /// <param name="ttl">
    /// TTL semantics: <see langword="null"/> uses the configured backstop; a positive value sets an
    /// explicit absolute expiration; <see cref="TimeSpan.Zero"/> means no time-based expiration (the
    /// entry lives until evicted for capacity or invalidated); a negative value is a signal not to
    /// cache — the call becomes a no-op.
    /// </param>
    public void Set<T>(
        string key, T value,
        IEnumerable<CancellationChangeToken>? invalidationTokens = null,
        TimeSpan? ttl = null)
    {
        Guard.NotWhiteSpace(key);

        // Negative TTL: an explicit "do not cache this" signal from the caller.
        if (ttl is { } requested && requested < TimeSpan.Zero)
            return;

        var entryOptions = new MemoryCacheEntryOptions().SetSize(1);

        // null -> backstop; Zero -> infinite (no time expiration); positive -> explicit.
        var effectiveTtl = ttl ?? _options.Expiration;
        if (effectiveTtl > TimeSpan.Zero)
            entryOptions.SetAbsoluteExpiration(effectiveTtl);

        if (invalidationTokens is not null)
        {
            foreach (var token in invalidationTokens)
                entryOptions.AddExpirationToken(token);
        }

        entryOptions.RegisterPostEvictionCallback(OnEviction);
        _cache.Set(key, value, entryOptions);
    }

    /// <summary>Removes a single entry.</summary>
    public void Remove(string key) => _cache.Remove(Guard.NotWhiteSpace(key));

    /// <summary>
    /// Derives the effective entry bound: an explicit positive <see cref="BoundedCacheOptions.SizeLimit"/>
    /// wins; otherwise a fraction of available memory (respecting container limits), clamped to
    /// <c>[MinEntries, MaxEntries]</c>.
    /// </summary>
    internal static long ResolveSizeLimit(BoundedCacheOptions options)
    {
        if (options.SizeLimit is { } explicitLimit && explicitLimit > 0)
            return explicitLimit;

        var availableBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var entryBytes = Math.Max(1, options.EstimatedEntryBytes);
        var derived = (long)(availableBytes * options.MemoryFraction / entryBytes);
        return Math.Clamp(derived, options.MinEntries, options.MaxEntries);
    }

    private void OnEviction(object key, object? value, EvictionReason reason, object? state)
    {
        if (reason == EvictionReason.Capacity)
            Interlocked.Increment(ref _evictions);
    }

    private static IEnumerable<Measurement<long>> Observe(Func<BoundedCache, long> read)
    {
        foreach (var cache in Live.Keys)
        {
            if (!cache._disposed)
                yield return new Measurement<long>(read(cache), cache._nameTag);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Live.TryRemove(this, out _);
        _cache.Dispose();
    }
}
