# Contributing to PokeTokenBar for Windows

Thanks for helping improve the Windows port. Bug reports, fixes, tests, local
usage providers, translations, and documentation updates are welcome.

## Prerequisites

- Windows 10 or newer, x64
- .NET 10 SDK

## Build and test

Run from the repository root:

```powershell
dotnet restore .\Windows\PokeTokenBar.Windows.sln
dotnet build .\Windows\PokeTokenBar.Windows.sln -c Release --no-restore
dotnet test .\Windows\PokeTokenBar.Windows.sln -c Release --no-build
```

Run build and test sequentially; they share build outputs. Use
`.\Windows\run-dev.ps1` to update and launch the installed app. It stops the
running app and uses the same `%LOCALAPPDATA%\PokeTokenBar` progress as the
installed release. It does not create isolated development state.

Reserve `PTB_DATA_DIR` for isolated automated tests; do not set it for normal
development or installed launches. Do not place personal state, logs, caches,
credentials, or release certificates in Git.

## Project boundaries

- `Windows/src/PokeTokenBar.Core`: provider-independent usage, Pokémon, wallet,
  persistence, and save-transfer behavior.
- `Windows/src/PokeTokenBar.Platform.Windows`: Windows paths, tray, registry,
  SQLite, process, and startup integration.
- `Windows/src/PokeTokenBar.Windows`: WPF views, windows, animation, and user input.
- `Windows/tests/PokeTokenBar.Core.Tests`: contract and regression tests.

Provider-specific parsing should stay behind `IUsageProvider`; generic totals and
companion progress must aggregate all providers without hard-coding one provider.
External values and imported saves must be validated at their trust boundaries.

## Pull requests

- Keep each change focused and include regression tests where behavior changes.
- Use Conventional Commit subjects such as `feat:`, `fix:`, `docs:`, or `test:`.
- Describe WPF UI changes and include a screenshot when visual judgment matters.
- Confirm the Release build and full test suite pass.

## Legal and privacy

Do not commit Pokémon artwork, sprites, audio, credentials, private logs, or user
data. Pokémon data and sprites must remain runtime downloads from PokéAPI. By
submitting a contribution, you agree that your original work is provided under
the repository's MIT license.
