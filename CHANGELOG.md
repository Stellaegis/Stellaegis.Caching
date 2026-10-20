# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [5.0.0] — 2026-10-20

The library joins the 5.0 line of the Stellaegis family: it takes `Stellaegis.Primitives` 5.0.0, whose 1.0.0 is
deprecated, and `Stellaegis.Primitives.Hosting`, and what a host checks at startup is checked at startup.

### Migrating from 1.0.0

- `TryGet<T>` no longer answers `false` for a key holding a value of another type: it fails, naming the key and the
  two types. A key shared by two consumers is a defect, not a miss. A stored `null` still reads as absent.
- A cache registered from configuration is validated when the host starts, by `BoundedCacheOptions.FindError()`:
  a bound and a compaction percentage that would remove no entry per compaction fail the host, naming the cache,
  where 1.0.0 failed the first resolution of the cache. A cache registered from a factory is still checked when it
  is created.
- The metrics are published by one meter for the process, each measurement tagged with `cache.name`, and a
  disposed cache stops reporting; 1.0.0 created a meter per cache. A listener adding the meter by name sees no
  difference.
- A key carries the compartment where the application has several: `$"{space.ToSegment()}:…"`, from
  `Stellaegis.Primitives`. The library knows nothing of compartments and checks nothing: it is the consumer's key.

### Added

- `BoundedCacheOptions.FindError()`, and the options implement `IValidatableOptions` of
  `Stellaegis.Primitives.Hosting`: every setting is checked — the bound, the percentages, the clamp, the expiration,
  and the bound against the compaction percentage.

### Changed

- **Breaking.** `TryGet<T>` fails on a key holding another type.
- **Breaking.** `Stellaegis.Primitives` 5.0.0 and `Stellaegis.Primitives.Hosting` 5.0.0 are referenced;
  `Microsoft.Extensions.*` 10.0.13.
- One meter for the process; a disposed cache stops reporting.

## [1.0.0] — 2026-10-12

First release as a standalone library.

### Added

- `BoundedCache`, an in-memory cache bounded by entry count, with a backstop time-to-live, change-token
  invalidation, and metrics on the `Stellaegis.Caching` meter.
- `BoundedCacheOptions`, which sets the bound explicitly or derives it from available memory. A bound
  and a compaction percentage that would remove no entry per compaction are refused.
- `CacheSegmentInvalidator`, which evicts every entry linked to a segment at once.
- `AddStellaegisBoundedCache`, which registers a cache as a keyed singleton, configured either from its
  entry of the section `Stellaegis:Caching` or from a factory.

[5.0.0]: https://github.com/Stellaegis/Stellaegis.Caching/releases/tag/v5.0.0
[1.0.0]: https://github.com/Stellaegis/Stellaegis.Caching/releases/tag/v1.0.0
