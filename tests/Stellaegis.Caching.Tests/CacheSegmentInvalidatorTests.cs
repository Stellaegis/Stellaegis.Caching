using Xunit;

namespace Stellaegis.Caching.Tests;

/// <summary>
/// Unit tests for <see cref="CacheSegmentInvalidator"/>: invalidating a segment evicts exactly the
/// <see cref="BoundedCache"/> entries linked to it, and leaves other segments untouched.
/// </summary>
public sealed class CacheSegmentInvalidatorTests
{
    [Fact]
    public void Invalidating_a_segment_evicts_only_its_entries()
    {
        using var cache = new BoundedCache("segment-test", new BoundedCacheOptions());
        var invalidator = new CacheSegmentInvalidator();

        cache.Set("a", 1, new[] { invalidator.GetToken("seg-1") });
        cache.Set("b", 2, new[] { invalidator.GetToken("seg-1") });
        cache.Set("c", 3, new[] { invalidator.GetToken("seg-2") });

        invalidator.Invalidate("seg-1");

        Assert.False(cache.TryGet<int>("a", out _));
        Assert.False(cache.TryGet<int>("b", out _));
        Assert.True(cache.TryGet<int>("c", out var kept));
        Assert.Equal(3, kept);
    }

    [Fact]
    public void Invalidating_an_unknown_segment_is_a_no_op()
    {
        var invalidator = new CacheSegmentInvalidator();

        var exception = Record.Exception(() => invalidator.Invalidate("never-seen"));

        Assert.Null(exception);
    }
}
