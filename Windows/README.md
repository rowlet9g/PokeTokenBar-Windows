# PokeTokenBar for Windows

This directory contains the Windows-only WPF port. The original Swift/macOS
implementation remains in `../Sources` and acts as the behavioral reference.
Feature parity and source-retirement decisions are tracked in
[`PORTING_STATUS.md`](PORTING_STATUS.md).

## Projects

- `src/PokeTokenBar.Core`: platform-independent usage and companion logic.
- `src/PokeTokenBar.Platform.Windows`: Windows tray, storage, process, and OS integration.
- `src/PokeTokenBar.Windows`: WPF application and views.
- `tests/PokeTokenBar.Core.Tests`: contract tests ported from the Swift test suite.

## Current milestone

- Windows notification-area icon and popup shell.
- A draggable borderless popup. Drag the title area to move it; the `—` button
  hides it to the notification area, and reopening preserves the session position.
- Single-instance guard using a named mutex.
- `%LOCALAPPDATA%/PokeTokenBar` data, cache, and log directories.
- Swift-compatible `TokenFormatter` with contract tests.
- Direct Codex JSONL scanning from `%USERPROFILE%\.codex\sessions` and
  `%USERPROFILE%\.codex\archived_sessions`.
- Direct Gemini CLI JSON/JSONL scanning from `%USERPROFILE%\.gemini\tmp`.
  Only token metadata is mapped; prompt content is neither retained nor shown.
- Direct Cursor stable/nightly SQLite scanning from roaming AppData, using the
  Windows inbox SQLite runtime and an incremental row watermark. Only
  `bubbleId:*` token metadata is read; flat-rate Cursor usage reports no cost.
- Direct GitHub Copilot CLI `session-store.db` scanning under `%USERPROFILE%\.copilot`
  (or `COPILOT_HOME`). Prompt cache columns are de-duplicated from the stored
  input total, and the subscription provider reports tokens without invented cost.
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
- Species/Catch log collection modes. The catch log keeps the active companion
  first, sorts graduated companions newest-first, and shows rarity, nature,
  shiny state, evolution sprites, and graduation time for each individual.
- Windows tray notifications for hatch, evolution, and graduation milestones.
  Clicking a milestone notification opens the PokeTokenBar popup.
- A persisted Settings tab for notification preferences, topmost behavior,
  refresh interval, and background launch at Windows login.
- Hatch, evolution, and graduation presentation overlays. Milestones that occur
  while the popup is hidden are queued and play the next time it opens.
- Shop and Bag tabs backed by a persisted token wallet and inventory. Rare
  Candy adds 100M companion XP, Mint rerolls nature, and the one-time Shiny
  Charm improves future hatch odds from 1/64 to 1/48. Purchases and item use
  require an inline confirmation.
- Paid fresh-egg rerolls at the original 1B/2.5B/4B balance for Basic,
  Uncommon-or-better, and Rare-or-better tiers. Releasing a shiny companion
  requires an additional warning step, and rolled rarity is revalidated before
  a guarantee is consumed.
- Settings can export a versioned JSON save or import one from another PC.
  Imports validate the format and numeric values, require explicit replacement
  confirmation, rebase this PC's daily usage ledger, and retain the five newest
  pre-import recovery backups beside `companion-state.json`.

Set `PTB_DATA_DIR` to override the data directory for isolated development and
smoke tests.

When launching from a Codex-integrated PowerShell session, use `run-dev.ps1`.
It keeps development state in the repository-local, git-ignored `.ptb-data`
directory so Windows sandbox virtualization cannot split the LocalAppData view:

```powershell
.\run-dev.ps1
```

The Codex parser is streaming and keeps a cache keyed by path, modification
time, and size. It reads only the metadata prefix of older candidate parent
sessions until a fork dependency actually needs the full file.

The current popup covers the complete core loop, collection/Pokédex, shop and
bag, milestone notifications and animations, runtime settings, startup, and
save transfer. Gemini, Cursor, and Copilot are available as additional local
providers; the separate floating pet and release packaging remain.

## Build

Requires the .NET 10 SDK and Windows 10 or newer.

```powershell
dotnet build .\PokeTokenBar.Windows.sln
dotnet test .\PokeTokenBar.Windows.sln
```

## Install for the current Windows user

`install.ps1` publishes a self-contained `win-x64` executable, installs it to
`%LOCALAPPDATA%\Programs\PokeTokenBar`, and creates PokeTokenBar shortcuts on
the desktop and in the Start menu:

```powershell
.\install.ps1
```

After installation, launch PokeTokenBar from either shortcut. Re-run the same
script whenever a newer local build should replace the installed executable.
On the first install, an existing `.ptb-data\companion-state.json` from
`run-dev.ps1` is copied into the Windows data directory when no installed state
exists yet. Existing installed progress is never overwritten.
