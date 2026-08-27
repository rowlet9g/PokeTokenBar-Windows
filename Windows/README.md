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
- First ported core utility (`TokenFormatter`) with tests.

Set `PTB_DATA_DIR` to override the data directory for isolated development and
smoke tests.

## Build

Requires the .NET 10 SDK and Windows 10 or newer.

```powershell
dotnet build .\PokeTokenBar.Windows.sln
dotnet test .\PokeTokenBar.Windows.sln
```
