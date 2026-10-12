using System.Collections.Concurrent;
using Microsoft.Extensions.Primitives;

namespace Stellaegis.Caching;

/// <summary>
/// Manages cache invalidation tokens grouped by <em>segment</em> — an arbitrary string key shared by a
/// set of cache entries (e.g. a user, a document, a configuration key). Cancelling a segment's token
/// evicts every entry linked to it at once.
/// </summary>
/// <remarks>
/// Attach the token to a <see cref="BoundedCache"/> entry through the change-token parameter of
/// <see cref="BoundedCache.Set{T}"/>. This is the mechanism; the policy — which change invalidates which
/// segment — belongs to the caller, typically a handler for a change notification.
/// </remarks>
public sealed class CacheSegmentInvalidator
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tokens = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or creates a change token for <paramref name="segment"/>. Attach it to the cache entries that
    /// should be evicted together when the segment is invalidated.
    /// </summary>
    public CancellationChangeToken GetToken(string segment)
    {
        var cts = _tokens.GetOrAdd(Guard.NotWhiteSpace(segment), _ => new CancellationTokenSource());
        return new CancellationChangeToken(cts.Token);
    }

    /// <summary>Evicts every cache entry linked to <paramref name="segment"/> by cancelling its token.</summary>
    public void Invalidate(string segment)
    {
        if (_tokens.TryRemove(Guard.NotWhiteSpace(segment), out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
