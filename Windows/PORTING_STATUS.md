# Windows porting status

The Swift/macOS tree remains a behavioral reference while the Windows product
is implemented independently in C# and WPF. Swift source files are not compiled
or loaded by the Windows solution.

## Feature matrix

| Area | Swift reference | Windows status | Decision / next work |
| --- | --- | --- | --- |
| Codex local usage | `LocalUsageReader`, `UsageStore` | Complete | Keep contract and performance tests in C#. |
| Today/week/month totals | `UsageStore`, `PopoverView` | Complete | Windows locale controls the week boundary. |
| Egg, hatch, growth, evolution | `CompanionStore`, `CompanionView` | Complete | C# owns persistence and balance rules. |
| PokéAPI, sprites, evolution trees | `PokeAPIClient`, `SpriteLoader` | Complete | Windows disk cache and URL validation are in place. |
| Pokédex and catch log | `CompanionView`, `PopoverView` | Complete | Only encountered forms are revealed. |
| Tray popup and single instance | `PokeTokenBarApp`, `SingleInstance` | Complete | Implemented with WPF, WinForms `NotifyIcon`, and a named mutex. |
| Milestone notifications | companion UI events | Complete | Windows tray notifications cover hatch, evolution, and graduation. |
| Persistence diagnostics | `AppLog`, companion storage | Complete | Diagnostics are written to logs and kept out of the user-facing popup. |
| Distribution | release scripts | Partial | Self-contained `win-x64` install and shortcuts exist; signing and a packaged installer remain. |
| Event animations | `SpriteAnimation`, `FloatingPetPanel` | Not ported | Add hatch/evolution presentation after notification behavior is stable. |
| Settings and startup | `SettingsView`, `LoginItem` | Not ported | Add refresh, topmost, launch-at-login, and notification preferences. |
| Bag and shop | `BagView`, `ShopView` | Not ported | Port only after the desired Windows game economy is confirmed. |
| Rare Candy, premium eggs, Shiny Charm | related Swift models/tests | Not ported | Port domain rules first, then WPF surfaces. |
| Additional usage providers | provider implementations and tests | Not ported | Codex is the only Windows provider today. Add providers individually. |
| Save transfer | `SaveTransfer` | Not ported | Define a versioned Windows import/export format before implementation. |
| Update checker | `UpdateChecker` | Not ported | Choose a signed release channel before enabling self-update. |
| Localization | `Localization` and localized UI tests | Not ported | Current Windows UI is Korean-first with some English labels. |
| macOS-only integration | AppKit, Keychain, Homebrew, login item | Replaced / excluded | Do not translate directly; use Windows equivalents only where required. |

## Source-retirement rule

Do not translate Swift files line by line. For each desired feature, extract its
behavior and tests, implement the Windows contract in C#, and verify it. The
macOS-only `Sources`, Swift tests, `Package.swift`, scripts, and `.icns` can be
removed from this Windows repository after every desired row above is either
complete or explicitly excluded. Shared fixtures, artwork, documentation,
license files, and behavioral test data should remain.

## Recommended order

1. Settings and Windows launch-at-login.
2. Hatch/evolution animations.
3. Bag, shop, and item rules.
4. Selected additional usage providers.
5. Signed installer, update channel, and final macOS-source retirement.
