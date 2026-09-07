# PokeTokenBar for Windows

This directory contains the Windows-only WPF product. The retired Swift/macOS
implementation remains available through Git history and the upstream project.
Current scope and remaining Windows work are tracked in
[`PORTING_STATUS.md`](PORTING_STATUS.md).

## Projects

- `src/PokeTokenBar.Core`: platform-independent usage and companion logic.
- `src/PokeTokenBar.Platform.Windows`: Windows tray, storage, process, and OS integration.
- `src/PokeTokenBar.Windows`: WPF application and views.
- `tests/PokeTokenBar.Core.Tests`: contract tests ported from the Swift test suite.

## Current milestone

- TOKENS tab with today/week/month provider totals and shares, today's input/output/cache
  breakdown, refresh status, and per-provider timestamps. Zero-usage providers are hidden.
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
- Direct Antigravity CLI/IDE SQLite scanning from
  `%USERPROFILE%\.gemini\antigravity*\conversations`. The protobuf metadata
  reader maps input, cache-read, output, and thinking tokens without reading
  transcript text. Locked active databases use a bounded temporary snapshot.
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
- An opt-in floating pet in Settings. It uses the current companion sprite (or
  egg), gently idles above normal windows, toggles the popup on click, persists
  a drag position across monitors, and offers disable on right-click.
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

`%LOCALAPPDATA%\PokeTokenBar` is the single canonical store for installed and
local-development progress. `PTB_DATA_DIR` remains available only for automated
isolated smoke tests.

Use `run-dev.ps1` to publish the latest local build over the installed app and
launch it against that canonical store:

```powershell
.\run-dev.ps1
```

The Codex parser is streaming and keeps a cache keyed by path, modification
time, and size. It reads only the metadata prefix of older candidate parent
sessions until a fork dependency actually needs the full file.

The current popup covers the complete core loop, collection/Pokédex, shop and
bag, milestone notifications and animations, runtime settings, startup, and
save transfer. Antigravity, legacy Gemini CLI, Cursor, and Copilot are available
as additional local providers. The floating pet is implemented and manually verified. Portable ZIP
and per-user Inno Setup packaging are implemented; Authenticode signing remains.

## Build

Requires the .NET 10 SDK and Windows 10 or newer.

```powershell
dotnet build .\PokeTokenBar.Windows.sln
dotnet test .\PokeTokenBar.Windows.sln
```

## Install for the current Windows user

`install.ps1` publishes a self-contained `win-x64` executable, installs it to
`%LOCALAPPDATA%\Programs\PokeTokenBar`, and creates a Start menu shortcut:

```powershell
.\install.ps1
```

After installation, launch PokeTokenBar from the Start menu or pin the running
app to the taskbar. Re-run the same script whenever a newer local build should
replace the installed executable. Existing progress in the canonical data
directory is never overwritten.

## Build release artifacts

The project version is declared in `Directory.Build.props`. The release script
always creates a self-contained portable ZIP and SHA-256 checksum. If Inno
Setup 7 is installed, it also creates a per-user installer:

```powershell
.\publish-release.ps1 -RequireInstaller
```

See [`../RELEASE.md`](../RELEASE.md) for the tag-based GitHub Release workflow
and the manual install/update/uninstall checks.
