using System.Diagnostics.Metrics;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Stellaegis.Caching.Tests;

/// <summary>
/// Unit tests for <see cref="BoundedCache"/>: the size bound is enforced, invalidation still works,
/// the auto-derived bound stays within its clamp, the TTL conventions hold, and metrics are emitted.
/// </summary>
public sealed class BoundedCacheTests
{
    [Fact]
    public async Task Bound_is_enforced_by_eviction()
    {
        const int limit = 100;
        const int inserted = 500;
        using var cache = new BoundedCache("bound-test", new BoundedCacheOptions { SizeLimit = limit });

        for (var i = 0; i < inserted; i++)
            cache.Set($"key:{i}", i);

        // MemoryCache overflow compaction runs on a background thread, so poll for it to settle.
        var count = await PollAsync(
            () => cache.GetStatistics()?.CurrentEntryCount ?? 0,
            settled => settled <= limit,
            TimeSpan.FromSeconds(5));

        Assert.InRange(count, 0, limit);
        Assert.True(cache.EvictionCount > 0);
    }

    [Fact]
    public void Explicit_size_limit_overrides_auto_derivation()
    {
        using var cache = new BoundedCache("explicit-size", new BoundedCacheOptions { SizeLimit = 4242 });

        Assert.Equal(4242, cache.SizeLimit);
    }

    [Fact]
    public void Bound_too_small_for_its_compaction_percentage_is_refused()
    {
        var act = () => new BoundedCache("too-small", new BoundedCacheOptions { SizeLimit = 19 });

        var exception = Assert.Throws<ArgumentException>(act);
        Assert.Equal("options", exception.ParamName);

        using var smallest = new BoundedCache("smallest", new BoundedCacheOptions { SizeLimit = 20 });
        Assert.Equal(20, smallest.SizeLimit);
    }

    [Fact]
    public void Small_bound_is_accepted_with_a_matching_compaction_percentage()
    {
        using var cache = new BoundedCache(
            "small", new BoundedCacheOptions { SizeLimit = 10, CompactionPercentage = 0.1 });

        Assert.Equal(10, cache.SizeLimit);
    }

    [Fact]
    public void Auto_derived_size_limit_stays_within_the_clamp()
    {
        var options = new BoundedCacheOptions { SizeLimit = null, MinEntries = 2000, MaxEntries = 50_000 };
        using var cache = new BoundedCache("auto-size", options);

        Assert.InRange(cache.SizeLimit, options.MinEntries, options.MaxEntries);
    }

    [Fact]
    public void Invalidation_token_evicts_all_linked_entries()
    {
        using var cache = new BoundedCache("invalidation-test", new BoundedCacheOptions());
        using var segment = new CancellationTokenSource();
        var token = new CancellationChangeToken(segment.Token);

        cache.Set("a", 1, new[] { token });
        cache.Set("b", 2, new[] { token });
        Assert.True(cache.TryGet<int>("a", out _));

        segment.Cancel(); // CacheSegmentInvalidator.Invalidate(segment) does exactly this

        Assert.False(cache.TryGet<int>("a", out _));
        Assert.False(cache.TryGet<int>("b", out _));
    }

    [Fact]
    public void Negative_ttl_is_not_cached()
    {
        using var cache = new BoundedCache("negative-ttl-test", new BoundedCacheOptions());

        cache.Set("nope", 1, ttl: TimeSpan.FromSeconds(-1));

        Assert.False(cache.TryGet<int>("nope", out _));
    }

    [Fact]
    public async Task Zero_ttl_survives_the_backstop_while_null_ttl_expires()
    {
        var backstop = TimeSpan.FromMilliseconds(50);
        using var cache = new BoundedCache("zero-ttl-test", new BoundedCacheOptions { Expiration = backstop });

        cache.Set("infinite", 1, ttl: TimeSpan.Zero); // no time-based expiration
        cache.Set("backstopped", 2);                  // null ttl -> uses the 50 ms backstop

        await Task.Delay(TimeSpan.FromMilliseconds(250));

        Assert.True(cache.TryGet<int>("infinite", out var infinite));
        Assert.Equal(1, infinite);
        Assert.False(cache.TryGet<int>("backstopped", out _));
    }

    [Fact]
    public void Metrics_are_emitted_on_the_meter_tagged_by_cache_name()
    {
        const string cacheName = "metrics-test";
        using var cache = new BoundedCache(cacheName, new BoundedCacheOptions { SizeLimit = 100 });

        var seen = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == BoundedCache.MeterName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "cache.name" && (string?)tag.Value == cacheName)
                    seen.Add(instrument.Name);
            }
        });
        listener.Start();

        cache.Set("k", 1);
        cache.TryGet<int>("k", out _);   // hit
        cache.TryGet<int>("miss", out _); // miss
        listener.RecordObservableInstruments();

        Assert.Superset(new HashSet<string>
        {
            "stellaegis.cache.entries",
            "stellaegis.cache.hits",
            "stellaegis.cache.misses",
            "stellaegis.cache.evictions",
        }, seen.ToHashSet());
    }

    [Fact]
    public void Keyed_caches_do_not_share_a_bound()
    {
        // Two independently-bounded caches: filling one must not evict the other's entries.
        using var filled = new BoundedCache("filled", new BoundedCacheOptions { SizeLimit = 50 });
        using var other = new BoundedCache("other", new BoundedCacheOptions { SizeLimit = 50 });

        other.Set("kept", 1);
        for (var i = 0; i < 500; i++)
            filled.Set($"key:{i}", i);

        Assert.True(other.TryGet<int>("kept", out var kept));
        Assert.Equal(1, kept);
    }

    // A key read as another type than it holds is two consumers sharing it: a failure, never a miss.
    [Fact]
    public void Reading_a_key_as_another_type_fails_rather_than_misses()
    {
        using var cache = new BoundedCache("typed", new BoundedCacheOptions());
        cache.Set("k", 1);

        var exception = Assert.Throws<InvalidOperationException>(() => cache.TryGet<string>("k", out _));

        Assert.Contains("holds a Int32, and a String is read", exception.Message);
        Assert.True(cache.TryGet<int>("k", out var kept));
        Assert.Equal(1, kept);
        Assert.False(cache.TryGet<string>("absent", out _));
    }

    [Fact]
    public void A_stored_null_reads_as_absent()
    {
        using var cache = new BoundedCache("nulls", new BoundedCacheOptions());
        cache.Set<string?>("k", null);

        Assert.False(cache.TryGet<string>("k", out _));
    }

    [Fact]
    public void Caches_share_one_meter_and_a_disposed_cache_stops_reporting()
    {
        var published = new HashSet<string>();
        var observed = new List<(string Instrument, string Cache)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == BoundedCache.MeterName)
                {
                    published.Add(instrument.Name);
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "cache.name" && tag.Value is string name)
                    observed.Add((instrument.Name, name));
            }
        });
        listener.Start();

        var first = new BoundedCache("shared-meter-first", new BoundedCacheOptions { SizeLimit = 100 });
        using var second = new BoundedCache("shared-meter-second", new BoundedCacheOptions { SizeLimit = 100 });
        listener.RecordObservableInstruments();

        Assert.Equal(4, published.Count);
        Assert.Contains(("stellaegis.cache.entries", "shared-meter-first"), observed);
        Assert.Contains(("stellaegis.cache.entries", "shared-meter-second"), observed);

        first.Dispose();
        observed.Clear();
        listener.RecordObservableInstruments();

        Assert.DoesNotContain(observed, o => o.Cache == "shared-meter-first");
        Assert.Contains(("stellaegis.cache.hits", "shared-meter-second"), observed);
    }

    private static async Task<T> PollAsync<T>(Func<T> read, Func<T, bool> settled, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var value = read();
        while (!settled(value) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            value = read();
        }

        return value;
    }
}
