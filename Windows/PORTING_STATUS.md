# Windows porting status

The desired macOS behavior has been audited and the Windows product is now
implemented independently in C# and WPF. The retired Swift source remains
available through Git history and the upstream project, not in the active tree.

## Feature matrix

### Save isolation fix: 0.5.2 (2026-09-09)

- Confirmed that AppData access from a Codex-launched process resolved to the
  Codex package's LocalCache, while ordinary Windows execution used real AppData.
  Earlier same-context persistence/installer checks did not detect this split.
- Default data now lives in `%USERPROFILE%\.poketokenbar`. First-run migration
  copies the legacy directory atomically and retains it; redirected legacy paths
  are rejected, and existing new data is never replaced by a later legacy import.
- Release build and 170 tests passed, including migration failure/retry tests.
- On the affected PC, the 0.5.2 installer was applied twice in ordinary Windows
  execution. Both legacy and new state hashes remained unchanged during installation.
- Ordinary installed execution, Codex-launched portable execution, and ordinary
  installed execution again all loaded species #603 from the same new directory.
- These checks cover process restarts and installer replacement, not a forced PC
  power cut. Already-divergent historical saves require deliberate recovery.

| Area | Windows status | Decision / next work |
| --- | --- | --- |
| Codex local usage | Complete | Replay-safe JSONL parsing and regression tests are in C#. |
| Codex SSH usage | Complete | Concrete aliases are discovered from SSH config; selected hosts sync token metadata incrementally, and local/remote copies of the same session are deduplicated. |
| Today/week/month totals | Complete | Windows locale controls the week boundary. |
| Provider usage dashboard | Complete | TOKENS shows period totals, provider shares, per-model totals/shares for all three periods, today input/output/cache, and refresh/error status. Model identifiers are retained from local records; missing identifiers are explicitly unknown. |
| Official usage limits | Partial | Codex app-server, Claude Code OAuth, and Antigravity Cloud Code 5-hour/weekly buckets are read and shown on HOME; other providers' official APIs remain outside this slice. |
| Egg, hatch, growth, evolution | Complete | C# owns persistence and balance rules. |
| Evolution branch selection and plan recovery | Implemented | Branches leading to uncompleted final forms are preferred. Complete saved routes are retained without rerolling; truncated routes are extended from the reached form. |
| Collection-aware hatch weights and repeat growth | Implemented in source | Completed base species use half the integer capture-rate weight (minimum 1); new repeat hatches save a 2x growth bonus. Old active saves retain their original stage costs. |
| Ditto disguise/reveal | Implemented | Common evolving hatches roll 1/128; first evolution reveals Ditto, preserving nature, shiny status and overflow. Disguised shiny appearance is hidden. |
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

### Collection balance (2026-10-02; source changes, not yet released)

- Reference: upstream `42df4e61590fd049b299ec21c349f6d84f93a96a`,
  `CollectionWeight.adjusted`, hatch-time `hasGrowthBoost`, and
  `PokemonBalance.phaseThreshold`.
- Indexed hatch selection halves completed base species' integer weights, with
  minimum 1. Multiple completed individuals do not apply the reduction repeatedly;
  released-only records do not count. Guaranteed eggs retain their rarity filter.
  The existing REST fallback remains unweighted, matching the upstream fallback.
- New hatches of a completed base species save `hasGrowthBoost`. Each rounded
  stage cost is divided by 2 and rounded away from zero, including graduation.
  Egg incubation, actual token usage, the usage ledger and wallet are unchanged.
- HOME shows an orange growth x2 badge. The saved bonus survives evolution,
  Ditto reveal, restart and save transfer; existing saves missing the flag remain
  unboosted rather than changing progress during an update.
- Release build passed with zero warnings/errors; all 240 tests passed, including
  19 collection-balance cases. An isolated WPF render confirmed the HOME badge.
  The pre-existing database-recovery test now dates its fixture in the current
  month so a calendar rollover does not hide it from the provider snapshot.

### Evolution selection and recovery (2026-09-30; source changes)

- Reference: upstream `42df4e61590fd049b299ec21c349f6d84f93a96a`,
  `CompanionStore.pickPlannedChild` and `normalizedEvolutionState`.
- Selection remains automatic: at every branch, prefer children with at least
  one final species not completed for that base species, then randomly choose
  within the candidate pool. Released companions do not count as completed.
- Valid saved plans remain unchanged, including collected branches, and consume
  no new random rolls at restart. Missing/invalid future plans are rebuilt from
  the reached form, retaining growth, shiny status and nature.
- Loaded/imported subjects accumulate tokens but wait for tree validation before
  applying evolution/graduation. Failed lookups retry at the next refresh. If
  metadata cannot explain an already reached path, it does not roll that path back.
- Release build passed with zero warnings/errors and 221 tests passed, including
  branch preference, released-history exclusion, stable restarts, recovery and
  offline progress retention. No user save or published installer was changed.

### Model breakdown and Ditto (2026-09-30; source changes)

- Reference: upstream `42df4e61590fd049b299ec21c349f6d84f93a96a`,
  `CompanionStore.dittoDisguiseHit` and `revealDitto`.
- Daily/week/month aggregation preserves per-model input/output/cache totals;
  TOKENS shows descending totals and provider-relative shares without adding tokens
  to the growth ledger. Missing model identifiers remain explicitly unknown.
- Disguise identity is persisted on new hatches only. Reveal metadata failures retain
  the disguise and progress for retry. Subject identity checks prevent a pending
  reveal from overwriting a newly purchased egg; import is serialized by the hatch gate.
- The rare single-form Ditto replaces the disguise in the active collection, carries
  first-stage overflow, and has its own notification and milestone overlay.
- Release build passed without warnings/errors and 214 tests passed. Isolated WPF
  previews verified all three model period views and the reveal overlay. No real
  user save was modified and no release has been issued for these source changes.

Antigravity Desktop 2.17 compatibility was validated on 2026-09-28: the app no
longer logs its server launch arguments. Discovery now reads the live server's
CSRF argument with a bounded hidden PowerShell query, verifying session, executable
path and ownership of the loopback port. Legacy log discovery remains supported.
The real provider returned both weekly quota buckets (55.6% Gemini, 100% external
models at validation time). All 202 tests passed, including injected live discovery,
invalid connection rejection and credential redaction. This change is prepared for release 0.6.3.

1. Finish official-limit resilience: retry backoff for HTTP 429 and authentication failures,
   account-change handling during failed requests, and actual Windows credential discovery validation.
2. Official-limit warnings, companion mood and candy rewards are connected (see validation below).
   Remaining: exhaustion forecasts, configurable thresholds, and independent notification/bubble settings.
3. Continue HOME provider selection and settings parity, then address localization and system theme support.
4. Validate the seven new local adapters against real tool sessions.
5. Add Authenticode signing and an update channel when the certificate and distribution policy are ready.

## Release 0.6.0 validation (2026-09-18)

- Added optional remote Codex usage collection with incremental metadata-only SSH sync.
- Concrete aliases are discovered from `%USERPROFILE%\.ssh\config`; pattern hosts are ignored and no remote host is selected automatically on a fresh install.
- Existing selected aliases remain selected after upgrading, while removed aliases remain visible until the user deselects them.
- Local and remote copies of the same Codex entries are deduplicated.
- Release build passed with zero warnings/errors; 191 tests passed, and the SSH selection view was rendered with empty and populated host lists.

## Release 0.6.1 validation (2026-09-22)

- Antigravity Desktop 2.15.1 exposes its authenticated official quota through the
  local language server `RetrieveUserQuotaSummary` method.
- PokeTokenBar discovers the current loopback HTTP port and CSRF value from the
  desktop app's own logs, keeps the CSRF value in memory, and never reads or logs
  the desktop OAuth access token, refresh token, client secret, prompts, or responses.
- A live account returned Gemini and third-party weekly utilization and reset times.
  Legacy CLI token-file support remains as a fallback when the desktop app is unavailable.
- Release build passed with zero warnings/errors and 194 tests passed, including
  local server discovery, header forwarding, wrapped response parsing, and fallback isolation.
- The self-contained `win-x64` portable archive was generated with product/file
  version 0.6.1, its SHA-256 matched `SHA256SUMS.txt`, and no user-state files
  were present in the publish directory. The tag workflow builds the installer.

## Antigravity 2.x token accounting fix (2026-09-23)

- Confirmed that official quota percentages and local growth tokens use separate
  sources: the Desktop language server supplies quota, while conversation SQLite
  stores supply exact token counters.
- Antigravity 2.x removed generation `created_at`; the reader now correlates each
  generation with `steps.metadata` by response or execution ID instead of assigning
  the conversation's old timestamp.
- Generation blobs are read whole because current Desktop sessions can place usage
  metadata after more than 1 MiB of cumulative transcript payload.
- Current model counters use input/output/cache-write/cache-read fields directly;
  the legacy split output layout remains supported.
- The affected live Desktop store changed from zero today entries to one entry with
  74,679 tokens at its recorded 2026-09-23 step time. Release build passed with zero
  warnings/errors and all 195 tests passed, including a greater-than-1-MiB regression.
- This fix is prepared for release 0.6.2.

## Provider expansion validation recorded on 2026-09-08

- Release build passed with zero warnings/errors; 152 tests passed.
- Synthetic JSONL/JSON and Windows SQLite fixtures cover the seven new adapters,
  duplicate/replayed records, cache refresh, partial records, and database failures.
- Existing Gemini CLI parsing remains enabled. Kiro is explicitly estimated.
- These checks do not establish live compatibility with every installed tool version.

## Official limit slice validated on 2026-09-08

- Codex app-server JSON-RPC parsing covers primary, secondary, multi-bucket,
  duplicate-bucket, plan, and reset fields.
- Claude Code OAuth credential and `api/oauth/usage` parsing covers legacy
  5-hour/weekly fields, model-scoped weekly fields, plan tier, duplicate
  session/weekly buckets, and expired credentials.
- Rate-limit refresh preserves the previous snapshot when a later request fails.
- HOME renders Codex and Claude Code utilization and reset countdowns when the
  corresponding local executable or OAuth credential is available on Windows.
- HOME renders Antigravity Gemini and third-party 5-hour/weekly quota buckets
  from the Cloud Code quota summary when its local OAuth token is available.

## Account lifecycle correction validated on 2026-09-08

- Full Release solution build passed with zero warnings/errors; 161 tests passed.
  This also completes the previously blocked Claude/Antigravity fixture test run.
- Missing/logged-out/expired Claude credentials remove the previous limit snapshot.
  Antigravity token-file removal does the same. Simulated transient server errors
  preserve the previous value with an error; subsequent login loads the new value.
- An explicit `CLAUDE_CONFIG_DIR` no longer falls back to a different default account.
- HOME shows each snapshot's original fetch timestamp and optional plan metadata.
- HTTP responses were simulated; live OAuth requests and visual UI validation remain pending.

## Home layout parity validated on 2026-09-08

- Compared upstream `PopoverView.swift`, `CompanionView.swift`, and `CompanionStore.lineNodes`
  with the supplied Mac screenshot. Primary navigation is Home/Shop/Bag/Collection;
  usage details and settings are separate utility buttons. Reopening returns to Home.
- Light popup layout places the sprite beside name, rarity, stage, nature and growth;
  evolution previews precede today's compact/exact tokens and weekly/monthly totals and costs.
- Only uniquely determined future evolution nodes are previewed. A branch remains unknown;
  previewing a form does not add it to realized history or the Pokédex.
- HOME scrolls when limit rows exceed the available height. Existing shop, bag, collection,
  settings and usage-detail views remain accessible with matching light colors.
- Release build: zero warnings/errors; 163 tests passed. An isolated WPF fixture rendered
  all six views, including the Charmander preview. User progress was not replaced.
- Full parity is still pending: provider selection/details on Home, complete status behaviors,
  full settings/localization and automatic light/dark appearance are separate remaining work.

## Release 0.5.0 validation (2026-09-09)

- Fixed Windows cmd/bat quoting and wait for Codex initialize response before querying limits.
- Actual user Codex account returned three official limit windows.
- Release build: zero warnings/errors; 165 tests passed, including batch launch paths with spaces.
- Installer keeps the same AppId and user data directory; existing saves remain separate from program files.

## Official-limit interaction (2026-09-15; source change, not a new release)

- Reference: upstream `b34673aa74f26375dffa3564caf730d0dfcbd171`,
  `UsageStore.evaluateLimitAlerts`, `CompanionStore.grantCandies` and `computeState`.
- Warning at 80%, critical at 95%; each rising tier triggers once, rearming below 80%.
  HOME companion mood and floating-pet tooltip use critical limits before activity/sleep state.
  The floating pet shows a six-second warning popup when notifications are enabled.
- A newly reached 100% grants one Rare Candy for session windows and five for windows
  longer than 24 hours. Claude model/scoped windows and Codex individual spend caps are excluded.
  Antigravity rewards use its 5-hour/weekly buckets only. Candy adds inventory, not usage tokens.
- The first observation of each window seeds already-full limits without retroactive rewards,
  including providers that become available after the first refresh (stricter than upstream's global seed).
- Reward/alert ledgers and inventory share the existing atomic save. Restart and changing
  reset timestamps do not repeat rewards. A fresh below-100 observation rearms rewards;
  failed/expired/nonfinite snapshots cannot rearm them. Import merges existing claim markers.
  Save failure rolls back the effect and retries on a later refresh.
- 186 tests passed; solution Release build had zero warnings/errors. An isolated WPF fixture
  rendered the critical HOME message and popup; no real account was driven to its limit.
- The existing notification switch controls both system alerts and pet popups. Rewards remain
  enabled when notifications are disabled. Animated mood changes, forecasts, configurable
  thresholds and separate switches are not claimed as complete by this slice.
