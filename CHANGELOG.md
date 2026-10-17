# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `Observable.choose` as a function of the `Observable` module; like every function of the library that works with optional values it takes a `voption` chooser, and the `option` variant is `ObservableOption.choose`
- `Observable.ofSeq (items)` and `Observable.ofSeq (items, cancellationToken)`, reachable after `open FSharp.Control.R3`
- `Observable.chunkByBoundaries` accepting window boundaries of any element type
- `rxqueryWith cancellationToken` and `RxQueryBuilder (cancellationToken)` to cancel the query operators that return a task

### Changed

- **Breaking:** `AwaitOperationConfiguration` cases are prefixed with `Await` (`AwaitSequential`, `AwaitParallel 4`, ...) so they no longer collide with `System.Threading.Tasks.Parallel`
- **Breaking:** the `OptionExtensions`/`ValueOptionExtensions` types are replaced by `Observable.choose`, which takes a `voption` chooser, and `ObservableOption.choose` for `option`; the `Observable.Extensions` type is replaced by `Observable.ofSeq`
- **Breaking:** after `open FSharp.Control.R3`, `Observable.choose` is the R3 function, like `Observable.map` and `Observable.filter` already were; use `Microsoft.FSharp.Control.Observable.choose` for `IObservable` and F# events
- **Breaking:** after `open FSharp.Control.R3`, the bare type name `Observable` refers to `FSharp.Control.R3.ObservableFactories.Observable`; write `open type R3.Observable` and extend `R3.Observable` by its full name
- **Breaking:** `rxquery` `sumBy` adds values of the projected type: it requires `(+) : 'Value * 'Value -> 'Value`
- `mapAsync` and `iterAsync` validate the concurrency limit of the options when they are called
- `chunkBySize` and `chunkBy` validate every chunk length when they are called and report the parameter and the rejected value

### Fixed

- `rxquery` emitted its elements on the thread pool, out of order and after the source had moved on; `yield` and `zero` are now synchronous
- `rxquery` `sumBy` passed `null` to the `(+)` of reference types
- `chunkBy` accepted a non-positive window length of `ChunkTimeSpanCount` and `ChunkMillisecondsCount`, which failed every element

## [0.3.1] - 2026-01-28

### Fixed

- Removed `AutoOpen` attribute from `ChunkConfiguration` to prevent type collision with `System.TimeSpan`

## [0.3.0] - 2024-12-24

### Added
* `empty`
* `singleton`
* `ofSeq`
* `ofAsync`
* `ofTask`
* `toArray`
* `toList`
* `toLookup`
* `catch`
* `chunkBy`
* `chunkBySize`
* `merge`
* `choose`
* `ofType`

## [0.2.0] - 2024-11-23

- Added some basic functions: `filter/where`, `bind`, `concat`, `distinct`, `mapi`, `skip`, `take`, …
- Added `rxquery { … }` builder

## [0.1.0] - 2024-11-18

First pre-release

### Added
- Initial implementation of the library
- `map`, `iter` and `length` functions

[Unreleased]: https://github.com/fsprojects/FSharp.Control.R3/compare/releases/0.3.1...HEAD
[0.3.1]: https://github.com/fsprojects/FSharp.Control.R3/compare/releases/0.3.0...releases/0.3.1
[0.3.0]: https://github.com/fsprojects/FSharp.Control.R3/compare/releases/0.2.0...releases/0.3.0
[0.2.0]: https://github.com/fsprojects/FSharp.Control.R3/compare/releases/0.1.0...releases/0.2.0
[0.1.0]: https://github.com/fsprojects/FSharp.Control.R3/releases/tag/releases/0.1.0
