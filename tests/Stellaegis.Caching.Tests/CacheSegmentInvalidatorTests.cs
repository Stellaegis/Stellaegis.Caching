using FluentAssertions;
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

        cache.TryGet<int>("a", out _).Should().BeFalse();
        cache.TryGet<int>("b", out _).Should().BeFalse();
        cache.TryGet<int>("c", out var kept).Should().BeTrue();
        kept.Should().Be(3);
    }

    [Fact]
    public void Invalidating_an_unknown_segment_is_a_no_op()
    {
        var invalidator = new CacheSegmentInvalidator();

        var act = () => invalidator.Invalidate("never-seen");

        act.Should().NotThrow();
    }
}
