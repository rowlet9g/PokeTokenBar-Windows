# PokeTokenBar for Windows — developer guide

The active product is C#/.NET 10 and WPF. User-facing features and provider paths
are documented in [`../README.md`](../README.md); completion status and remaining
work are in [`PORTING_STATUS.md`](PORTING_STATUS.md).

## Architecture

- `src/PokeTokenBar.Core`: usage models, local token metadata parsing, aggregation,
  official rate-limit models, Pokémon progression, wallet, persistence, and save transfer.
- `src/PokeTokenBar.Platform.Windows`: Windows paths, SQLite-backed providers,
  Codex app-server, Claude Code OAuth, and Antigravity Cloud Code rate limits,
  tray, single-instance activation, and login startup registration.
- `src/PokeTokenBar.Windows`: WPF views and application lifetime. `App.xaml.cs`
  connects providers, refresh events, companion state, tray, and floating pet.
- `tests/PokeTokenBar.Core.Tests`: parsing, aggregation, progression, persistence,
  save-transfer, and Windows integration regression tests.

## Usage display and progression

`UsageStore.Snapshots` supplies provider data to HOME and TOKENS. HOME displays
all providers' daily total; `MainWindow.Tokens.cs` renders provider totals and
shares for TODAY, WEEK, and MONTH. TODAY additionally shows input, output, and
cache write/read counts. Weekly and monthly snapshots contain total tokens and
cost only, so their UI does not promise an input/output/cache breakdown.

Zero-usage providers are hidden for the selected period. Refresh status and each
provider's fetch time are displayed. A failed provider can retain its previous
snapshot, with an error notice indicating that older values may be included.

HOME also shows Codex's official 5-hour and weekly utilization when the local
Codex executable can answer `account/rateLimits/read`, and Claude Code's official
5-hour/weekly windows when its OAuth credential file is available. Claude Code
credentials are read from `CLAUDE_CONFIG_DIR\.credentials.json`,
`%USERPROFILE%\.claude\.credentials.json`, or
`%USERPROFILE%\.config\claude\.credentials.json`; the OAuth token is used only
in memory for the HTTPS request and is never written to PokeTokenBar logs or state.
Antigravity official quota is read from the Cloud Code `retrieveUserQuotaSummary`
endpoint, trying `CLOUD_CODE_URL` (when set), the daily endpoint, and the primary
endpoint. Its token file is read from `PTB_ANTIGRAVITY_TOKEN_FILE`,
`ANTIGRAVITY_TOKEN_FILE`, `%USERPROFILE%\.gemini\jetski-standalone-oauth-token`,
or `%USERPROFILE%\.gemini\antigravity\jetski-standalone-oauth-token`.
Reset countdowns are calculated from each provider response. If a request fails,
the last successful limit snapshot remains visible with an error status.

Twelve local providers are registered. Kiro CLI uses text-length estimates and
is labelled accordingly in TOKENS; its estimates also contribute to growth.
See [local provider accounting](../docs/reference/local-providers.md) for formats,
path overrides, and live-validation limitations.

`CompanionStore` records a per-provider baseline on first discovery, then uses
new daily increments for growth. Statistics periods do not reset Pokémon
progress. Parsing and UI changes must preserve that accounting boundary.

## Data and execution

`%LOCALAPPDATA%\PokeTokenBar` is the canonical store for installed and local
builds. It contains `companion-state.json`, `settings.json`, caches, and logs.
Installation, update, and uninstall preserve this directory. Save export/import
in Settings transfers progress between PCs; there is no automatic cloud sync.

Reserve `PTB_DATA_DIR` for isolated automated tests. Normal development uses the
canonical store and must not initialize or replace the user's existing progress.

Run the following commands from this `Windows` directory:

```powershell
dotnet restore .\PokeTokenBar.Windows.sln
dotnet build .\PokeTokenBar.Windows.sln -c Release --no-restore
dotnet test .\PokeTokenBar.Windows.sln -c Release --no-build
```

Build and test sequentially because they share output files.

```powershell
.\run-dev.ps1
```

This stops the running app, publishes a self-contained `win-x64` build into
`%LOCALAPPDATA%\Programs\PokeTokenBar`, updates the Start menu shortcut, and
launches the installed executable using existing progress. For installation
without launch, use `.\install.ps1`. Normal launch shows the main window;
login startup uses `--background` to restore the tray and floating pet.

## Packaging

`Directory.Build.props` owns the product version. To build an installer and ZIP
with Inno Setup 7 installed:

```powershell
.\publish-release.ps1 -RequireInstaller
```

Artifacts are generated under `artifacts/release/<version>` and ignored by Git.
See [`../RELEASE.md`](../RELEASE.md) for release tags, state-preservation checks,
and the per-release checklist. Code signing and in-app updates remain pending.
