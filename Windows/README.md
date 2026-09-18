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

Official limits now drive 80%/95% edge-triggered warnings, companion mood text and a
six-second floating-pet popup. `RateLimitStore.FreshSnapshots` excludes failed requests'
preserved values from these effects. `CompanionStore.Limits.cs` atomically records candy
claims with inventory: session limits grant one Rare Candy and weekly limits grant five
on a new 100% crossing. Initial full windows are seeded without retroactive rewards.
The notification switch suppresses alerts and popups, but does not suppress rewards.
These interactions shipped before 0.6.0; release 0.6.0 adds remote Codex SSH usage.

`UsageStore.Snapshots` supplies provider data to HOME and TOKENS. HOME displays
all providers' daily total; `MainWindow.Tokens.cs` renders provider totals and
shares for TODAY, WEEK, and MONTH. TODAY additionally shows input, output, and
cache write/read counts. Weekly and monthly snapshots contain total tokens and
cost only, so their UI does not promise an input/output/cache breakdown.

Zero-usage providers are hidden for the selected period. Refresh status and each
provider's fetch time are displayed. A failed provider can retain its previous
snapshot, with an error notice indicating that older values may be included.

SETTINGS discovers concrete SSH aliases from `%USERPROFILE%\.ssh\config` and lets
the user opt in to remote Codex session collection. The remote host
must support non-interactive OpenSSH authentication and provide `python3`. Only
session identity, model, and token-count metadata is synchronized; prompt and
response text remains on the remote host. After the first scan, the cache advances
by byte offset, and remote entries already present locally are deduplicated.
Remote collection currently supports Codex sessions only because each AI tool uses
different storage paths and record formats.

HOME also shows Codex's official 5-hour and weekly utilization when the local
Codex executable can answer `account/rateLimits/read`. Launch it with
`codex app-server`, which uses stdio by default; `--stdio` is unsupported and
causes the process to exit before returning limits. Token totals are read separately
from local session files, so they can still update when the limit request fails.
HOME also shows Claude Code's official
5-hour/weekly windows when its OAuth credential file is available. Claude Code
credentials are read exclusively from `CLAUDE_CONFIG_DIR\.credentials.json` when
that directory is configured; otherwise they are read from
`%USERPROFILE%\.claude\.credentials.json` or
`%USERPROFILE%\.config\claude\.credentials.json`; the OAuth token is used only
in memory for the HTTPS request and is never written to PokeTokenBar logs or state.
Antigravity official quota is read from the Cloud Code `retrieveUserQuotaSummary`
endpoint, trying `CLOUD_CODE_URL` (when set), the daily endpoint, and the primary
endpoint. Its token file is read from `PTB_ANTIGRAVITY_TOKEN_FILE`,
`ANTIGRAVITY_TOKEN_FILE`, `%USERPROFILE%\.gemini\jetski-standalone-oauth-token`,
or `%USERPROFILE%\.gemini\antigravity\jetski-standalone-oauth-token`.
Reset countdowns are calculated from each provider response. If a request fails,
the last successful limit snapshot remains visible with an error status and its
original fetch time. A provider returning no current credentials or visible limits
clears its old snapshot, so logging out does not leave an old account's values
displayed as freshly updated. HOME also displays plan metadata when available.

Twelve local providers are registered. Kiro CLI uses text-length estimates and
is labelled accordingly in TOKENS; its estimates also contribute to growth.
See [local provider accounting](../docs/reference/local-providers.md) for formats,
path overrides, and live-validation limitations.

Usage refresh defaults to 30 seconds. Settings offers 30 seconds, 1 minute,
2 minutes, 5 minutes, and 10 minutes. Concurrent refresh requests are serialized.
Transient refresh and UI-dispatch errors are recorded in `Logs/runtime.log` instead
of terminating the tray process. The same log records process start and exit events.

`CompanionStore` records a per-provider baseline on first discovery, then uses
new daily increments for growth. Statistics periods do not reset Pokémon
progress. Parsing and UI changes must preserve that accounting boundary.

## Data and execution

`%USERPROFILE%\.poketokenbar` is the canonical store for installed and local
builds. It contains `companion-state.json`, `settings.json`, caches, and logs.
The first 0.5.2+ launch copies a legacy `%LOCALAPPDATA%\PokeTokenBar` store without
removing it. Installation, update, and uninstall preserve both locations. Save export/import
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
