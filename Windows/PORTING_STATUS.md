# Windows porting status

The desired macOS behavior has been audited and the Windows product is now
implemented independently in C# and WPF. The retired Swift source remains
available through Git history and the upstream project, not in the active tree.

## Feature matrix

| Area | Windows status | Decision / next work |
| --- | --- | --- |
| Codex local usage | Complete | Replay-safe JSONL parsing and regression tests are in C#. |
| Today/week/month totals | Complete | Windows locale controls the week boundary. |
| Provider usage dashboard | Complete | TOKENS shows period totals, provider shares, today input/output/cache, and refresh/error status. A live Antigravity request was user-verified to increase both tokens and companion progress on 2026-09-07. |
| Official usage limits | Partial | Codex app-server 5-hour/weekly buckets are read and shown on HOME; Claude and Antigravity official limit adapters remain next parity work. |
| Egg, hatch, growth, evolution | Complete | C# owns persistence and balance rules. |
| PokéAPI, sprites, evolution trees | Complete | Windows disk cache and URL validation are in place. |
| Pokédex and catch log | Complete | Only encountered forms are revealed. |
| Tray popup and single instance | Complete | WPF, WinForms `NotifyIcon`, and a named mutex are used. |
| Milestone notifications and animations | Complete | Hatch, evolution, graduation, and queued overlays are implemented. |
| Floating pet | Complete | Click toggle, drag persistence, sizing, and disable menu are implemented. |
| Persistence diagnostics | Complete | Diagnostics remain in logs rather than the user-facing popup. |
| Settings and startup | Complete | Refresh, topmost, notifications, floating pet, and login startup persist. |
| Bag and shop | Complete | Wallet, inventory, confirmations, items, and paid egg rerolls are implemented. |
| Save transfer | Complete | Versioned import/export, validation, rebasing, and recovery backups are implemented. |
| Usage providers | Implemented; new adapters need live validation | Twelve local providers: existing five plus Claude Code, OpenCode, Hermes Agent, Grok CLI, Kiro CLI (estimated), Pi Agent, and omp. See the local-provider reference for accounting limitations. |
| Distribution | Partial | ZIP, checksums, installer, and tag-triggered release workflow are implemented; this does not imply a published release. Authenticode signing remains. |
| Update checker | Not ported | Choose a signed release channel before enabling updates. |
| Localization | Not ported | The Windows UI is Korean-first with some English labels. |
| macOS-only integration | Excluded | AppKit, Keychain, Homebrew, and macOS login-item behavior are outside scope. |
| Swift/macOS source retirement | Complete | Swift code, tests, build scripts, `.icns`, and obsolete docs were removed after fixture migration. |

## Validation recorded on 2026-09-07

- TOKENS implementation: Release build passed with zero warnings/errors; 128 tests passed.
- The user confirmed live Antigravity usage increases both the app's token count
  and Pokémon progress. Today's detail display is implemented, not a separate pending step.
- The installer handoff records successful install/update/uninstall/reinstall
  checks with user-state SHA-256 preservation. Repeat the release checklist for
  each new artifact; this is not a claim that every interactive installer screen
  or SmartScreen prompt has been checked.

## Remaining work

1. Decide the release version and publish its matching tag when ready.
2. Add Authenticode signing and choose a signed update channel before implementing updates.
3. Validate the seven new adapters against real tool sessions before releasing the provider expansion; decide whether UI localization is needed.

## Provider expansion validation recorded on 2026-09-08

- Release build passed with zero warnings/errors; 152 tests passed.
- Synthetic JSONL/JSON and Windows SQLite fixtures cover the seven new adapters,
  duplicate/replayed records, cache refresh, partial records, and database failures.
- Existing Gemini CLI parsing remains enabled. Kiro is explicitly estimated.
- These checks do not establish live compatibility with every installed tool version.

## Official limit slice validated on 2026-09-08

- Codex app-server JSON-RPC parsing covers primary, secondary, multi-bucket,
  duplicate-bucket, plan, and reset fields.
- Rate-limit refresh preserves the previous snapshot when a later request fails.
- HOME renders Codex 5-hour/weekly utilization and reset countdowns when a Codex
  executable is available on Windows.
