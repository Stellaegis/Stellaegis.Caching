# Stellaegis.Caching

Stellaegis.Caching is a small .NET library that provides an in-memory cache with a bounded number
of entries. Each cache has its own bound, its own expiration policy and its own metrics, so that
several caches can live in one process without competing for the same memory. Entries are
invalidated on demand, one by one or by group, and a time-to-live acts as a safety net behind that
invalidation.

The library targets .NET 10 and is released under the MIT license.

```sh
dotnet add package Stellaegis.Caching
```

## What the cache does

`BoundedCache` wraps a dedicated `MemoryCache` from `Microsoft.Extensions.Caching.Memory`. It adds
three things to it:

- **a bound on the number of entries.** Every entry counts as one, whatever the size of its value.
  The count never exceeds the bound. A write into a full cache is dropped — `Set` returns normally
  and the value is not stored — and the underlying `MemoryCache` starts a compaction on a background
  thread. The compaction removes `CompactionPercentage` of the entries, 5 % by default: expired
  entries first, then the least recently used ones. Writes are accepted again once it has run;
- **invalidation by change token.** When you store an entry, you can attach tokens to it. When a
  token is cancelled, every entry attached to it is evicted;
- **metrics**, published on the `Stellaegis.Caching` meter.

```csharp
using var cache = new BoundedCache("documents", new BoundedCacheOptions { SizeLimit = 10_000 });

cache.Set("document:42", document);

if (cache.TryGet<Document>("document:42", out var cached))
{
    // use cached
}

cache.Remove("document:42");
```

`TryGet<T>` returns `false` when the key is absent, and also when the stored value is not a `T`.

The bound counts entries, not bytes. This keeps the bound cheap to enforce and easy to predict, but
it assumes values of comparable size. If one cache holds values of very different sizes, give it a
bound that fits the largest ones, or split it into several caches.

## Freshness: invalidation first, expiration as a backstop

The cache does not know when the data behind an entry changes. Your code does, so your code
invalidates the entry: it calls `Remove`, or cancels a token attached to the entry. The
time-to-live only limits how long an entry can stay stale if an invalidation is missed. Once the
number of entries is bounded, that time-to-live can be long.

The `ttl` parameter of `Set` takes four kinds of values:

| `ttl` | Effect |
| --- | --- |
| omitted (`null`) | the entry expires after `BoundedCacheOptions.Expiration`, 5 minutes by default |
| positive | the entry expires after that duration |
| `TimeSpan.Zero` | the entry never expires by time; it leaves only when evicted for capacity or invalidated |
| negative | the value is not stored at all; the call does nothing |

The negative value lets a caller decide, per value, that something should not be cached — an empty
result, for instance — without a separate code path.

## Invalidating a group of entries: segments

A segment is a string shared by a set of entries: a user identifier, a document identifier, a
configuration key. `CacheSegmentInvalidator` holds one token per segment. Attach the segment's token
to the entries that depend on it, then invalidate the segment to evict all of them at once.

```csharp
var segments = new CacheSegmentInvalidator();

cache.Set($"profile:{userId}", profile, [segments.GetToken($"user:{userId}")]);
cache.Set($"settings:{userId}", settings, [segments.GetToken($"user:{userId}")]);

// Later, when that user's data changes:
segments.Invalidate($"user:{userId}");
```

An entry can depend on several segments: pass several tokens. Invalidating a segment that has no
token yet does nothing. After an invalidation, the next call to `GetToken` for that segment returns a
new token.

The library decides nothing about **when** a segment is invalidated. That decision belongs to your
code: typically a handler that receives a change notification from your database, your message bus
or your own services. `CacheSegmentInvalidator` is not registered by the library; register it
yourself, usually as a singleton next to the cache it serves.

## Sizing the cache

Set `SizeLimit` to fix the bound explicitly. Leave it unset, and the bound is derived when the cache
is created, from the memory available to the process (container limits included):

```text
bound = available memory × MemoryFraction ÷ EstimatedEntryBytes, clamped to [MinEntries, MaxEntries]
```

| Option | Default | Role |
| --- | --- | --- |
| `SizeLimit` | unset | the bound, in entries; when set, the four options below are ignored |
| `MemoryFraction` | `0.05` | the share of available memory the derived bound may claim |
| `EstimatedEntryBytes` | `1024` | the estimated size of one entry, in bytes |
| `MinEntries` | `1024` | the lowest derived bound |
| `MaxEntries` | `1000000` | the highest derived bound |
| `Expiration` | 5 minutes | the time-to-live of entries stored without their own |
| `CompactionPercentage` | `0.05` | the share of entries removed by one compaction |

The derived bound is an estimate: the library does not measure the actual size of your values. Read
the effective bound from `BoundedCache.SizeLimit`.

A compaction removes `SizeLimit × CompactionPercentage` entries, rounded down. Below one entry, it
removes nothing but expired entries, and a full cache keeps refusing writes until some of its
entries expire or are invalidated. With the default percentage, keep `SizeLimit` at 20 or more.

## Registering caches with dependency injection

`AddStellaegisBoundedCache` registers a cache as a keyed singleton. The key names the cache. It is
used to resolve the cache, to find its configuration, and to tag its metrics.

```csharp
builder.Services.AddStellaegisBoundedCache("permissions", builder.Configuration);
builder.Services.AddStellaegisBoundedCache("documents", builder.Configuration);
```

Each cache reads its own entry of the section `Stellaegis:Caching`:

```json
{
  "Stellaegis": {
    "Caching": {
      "permissions": { "SizeLimit": 10000, "Expiration": "00:30:00" },
      "documents": { "MemoryFraction": 0.02 }
    }
  }
}
```

A cache with no entry in the section uses the default options. Pass `sectionName` to read another
section. Because the key is also a configuration path segment, a key that contains `:` points to a
nested section.

The options are .NET named options, registered under the cache's key. To adjust them from code after
the configuration has been read, use `services.PostConfigure<BoundedCacheOptions>("permissions", o => ...)`.
To build them entirely in code, use the overload that takes a factory:

```csharp
builder.Services.AddStellaegisBoundedCache("permissions", _ => new BoundedCacheOptions { SizeLimit = 10_000 });
```

Resolve a cache by its key:

```csharp
public sealed class PermissionReader([FromKeyedServices("permissions")] BoundedCache cache) { ... }
```

Registering the same key twice keeps the first registration. The container disposes the cache when
it is itself disposed.

## Metrics

Every cache publishes four instruments on the meter `Stellaegis.Caching`, each tagged with
`cache.name`, whose value is the cache's name:

| Instrument | Kind | Meaning |
| --- | --- | --- |
| `stellaegis.cache.entries` | gauge | entries currently held |
| `stellaegis.cache.hits` | counter | reads that found a value since the cache was created |
| `stellaegis.cache.misses` | counter | reads that found nothing since the cache was created |
| `stellaegis.cache.evictions` | counter | entries removed for capacity, writes dropped by a full cache included |

The instruments are observable: their values are read when a listener collects them, so they cost
nothing when no one listens. To export them with OpenTelemetry, add the meter by name:
`.WithMetrics(m => m.AddMeter(BoundedCache.MeterName))`.

The same counts are available in code: `GetStatistics()` returns the underlying `MemoryCache`
statistics, and `EvictionCount` the number of capacity evictions.

## Building and testing

```sh
dotnet test Stellaegis.Caching.slnx
```

The tests need no external service.

## License

MIT — see [LICENSE](https://github.com/Stellaegis/Stellaegis.Caching/blob/main/LICENSE). The package depends on third-party components listed in
[THIRD-PARTY-NOTICES.md](https://github.com/Stellaegis/Stellaegis.Caching/blob/main/THIRD-PARTY-NOTICES.md).
