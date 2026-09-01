# PokeTokenBar Windows project guidance

This repository contains the Windows-only PokeTokenBar product. The macOS/Swift
reference tree has been retired; consult Git history or the upstream project
instead of reintroducing platform-specific source.

## Required checks

```powershell
dotnet build .\Windows\PokeTokenBar.Windows.sln -c Release
dotnet test .\Windows\PokeTokenBar.Windows.sln -c Release --no-build
```

- Keep provider-independent usage and companion behavior in `PokeTokenBar.Core`.
- Keep Windows filesystem, tray, registry, SQLite, and process integration in
  `PokeTokenBar.Platform.Windows`.
- Keep WPF views and window behavior in `PokeTokenBar.Windows`.
- Preserve `%LOCALAPPDATA%\PokeTokenBar` across install, update, and uninstall.
- Never commit Pokémon assets; species data and sprites remain runtime downloads.
- Keep release credentials and signing keys out of the repository.

## Release boundary

Follow `RELEASE.md`. A `vMAJOR.MINOR.PATCH` tag creates a GitHub Release, so show
the version and release-note summary for explicit approval before pushing a tag.
