# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[1.0.0]: https://github.com/Stellaegis/Stellaegis.Caching/releases/tag/v1.0.0
