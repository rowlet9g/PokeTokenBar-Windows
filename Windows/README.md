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

Set `PTB_DATA_DIR` to override the data directory for isolated development and
smoke tests.

The Codex parser is streaming and keeps a cache keyed by path, modification
time, and size. It reads only the metadata prefix of older candidate parent
sessions until a fork dependency actually needs the full file.

## Build

Requires the .NET 10 SDK and Windows 10 or newer.

```powershell
dotnet build .\PokeTokenBar.Windows.sln
dotnet test .\PokeTokenBar.Windows.sln
```
