# PokeTokenBar for Windows

This directory contains the Windows-only WPF port. The original Swift/macOS
implementation remains in `../Sources` and acts as the behavioral reference.

## Projects

- `src/PokeTokenBar.Core`: platform-independent usage and companion logic.
- `src/PokeTokenBar.Platform.Windows`: Windows tray, storage, process, and OS integration.
- `src/PokeTokenBar.Windows`: WPF application and views.
- `tests/PokeTokenBar.Core.Tests`: contract tests ported from the Swift test suite.

## Current milestone

- Windows notification-area icon and popup shell.
- Single-instance guard using a named mutex.
- `%LOCALAPPDATA%/PokeTokenBar` data, cache, and log directories.
- Swift-compatible `TokenFormatter` with contract tests.
- Direct Codex JSONL scanning from `%USERPROFILE%\.codex\sessions` and
  `%USERPROFILE%\.codex\archived_sessions`.
- Replay-safe Codex aggregation for normal sessions, manual forks, and
  subagents, including cumulative-counter resets and duplicate snapshots.
- Today, week, and month usage in the WPF popup, with active five-hour usage
  retained in the provider snapshot for later forecast and companion features.
- Manual refresh plus a two-minute background refresh; today's compact total
  is also exposed in the notification-area tooltip.
- Swift-compatible fresh-egg incubation: the first observed usage is stored as
  an install baseline, then only new per-provider token deltas advance the egg.
- Atomic companion-state persistence in the Windows data directory, including
  provider regressions, partial snapshots, date rollover, and corrupt-file
  backup handling.
- Native WPF vector egg and incubation progress, avoiding the monochrome emoji
  rendering used by the first shell milestone.
- PokéAPI-backed hatching across supported Gen 1–5 species. Base species are
  weighted by official capture rate, while Ditto remains outside the normal
  hatch pool to match the Swift behavior.
- Real evolution trees, Korean-first species names, 25 natures, a 1-in-64
  shiny roll, overflow-safe growth, branching evolution, and graduation into
  the persisted Pokédex data.
- Runtime PNG sprites from the official PokeAPI sprites repository. Species,
  evolution, base-index, and sprite responses are cached under the Windows
  cache directory; no Pokémon artwork is bundled in the executable.
- Strict evolution-chain URL validation and an offline-friendly cache. A stale
  base index remains usable when GraphQL is unavailable, with a bounded REST
  fallback when no index exists yet.
- Home/Pokédex popup tabs, a hidden-future evolution line, and sprite-backed
  species cards. The Pokédex shows only forms actually reached by the active
  companion plus every form permanently registered by graduated companions.

Set `PTB_DATA_DIR` to override the data directory for isolated development and
smoke tests.

The Codex parser is streaming and keeps a cache keyed by path, modification
time, and size. It reads only the metadata prefix of older candidate parent
sessions until a fork dependency actually needs the full file.

The current popup covers the complete core loop: egg, hatch, growth, evolution,
graduation, a fresh egg, and the first collection/Pokédex view. The next
porting layer is the catch log, event animations and notifications,
inventory/shop, settings, and the remaining usage providers.

## Build

Requires the .NET 10 SDK and Windows 10 or newer.

```powershell
dotnet build .\PokeTokenBar.Windows.sln
dotnet test .\PokeTokenBar.Windows.sln
```
