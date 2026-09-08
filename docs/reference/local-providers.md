# Local provider accounting

The Windows provider expansion follows the formats in
[chattymin/PokeTokenBar at b34673a](https://github.com/chattymin/PokeTokenBar/tree/b34673aa74f26375dffa3564caf730d0dfcbd171)
(`LocalUsageReader.swift` and `LocalAdditionalUsageProvider.swift`). Upstream
copyright remains covered by the repository's MIT license.

## Scope

The existing Codex, legacy Gemini CLI, Antigravity CLI/IDE, Cursor, and Copilot CLI
adapters remain enabled. Seven additional adapters bring the list to twelve.
This reads local tool records; it does not connect Claude/Grok web subscriptions,
mobile chats, account quotas, or cloud usage dashboards. The new adapters do not
report subscription costs.

| Adapter | Records and accounting |
| --- | --- |
| Claude Code | Assistant `message.usage`; input, output, cache creation/read. Repeated message/request IDs keep the largest total and earliest timestamp. Includes discoverable embedded Claude Code project directories under the Windows Claude app data folder. |
| OpenCode | `opencode.db` (or channel databases when absent), plus legacy `storage/message` JSON. Message IDs deduplicate across both formats; input/output/cache counters come from `tokens`. |
| Hermes Agent | `state.db` sessions table; session counters, including output plus reasoning tokens. Usage is assigned to the session start date, so sessions crossing midnight can differ from per-request daily totals. |
| Grok CLI | Session `updates.jsonl` completed turns. Replayed turns and child sessions marked `subagent` in `summary.json` are excluded because the parent includes them. Camel-case input includes cache; snake-case input excludes cache. |
| Pi Agent | Session message, compaction and branch-summary usage. Aborted/error messages are excluded. Reasoning is already included in output. |
| omp | Assistant usage with session-scoped IDs; `bridge` conversion copies are excluded and subagent logs remain included. |
| Kiro CLI | SQLite conversation history, CLI JSONL, and v3 `messages.jsonl` plus companion metadata. **Estimated** from UTF-8 text bytes divided by four, following upstream's approach. Credits are not token counts. |

Pi/omp total-only records fall into the input bucket; OpenCode/Grok totals above
the available breakdown add the residual to output. These fallbacks preserve
totals but cannot recover an exact input/output split.

Kiro estimates exclude image payloads and cannot account for hidden model context.
They contribute to displayed totals and Pokémon growth, with an estimate label
and explanation in TOKENS. Cleared Kiro turns already read are retained in memory
while the app runs; restarting cannot reconstruct deleted source records. Saved
Pokémon progress is independent of this in-memory history.

## Optional search roots

Default paths are listed in the root README. The following environment variables
add search locations; restart the app after changing its environment.

| Adapter | Tool variables |
| --- | --- |
| Claude Code | `CLAUDE_CONFIG_DIR` (append `projects`) |
| OpenCode | `OPENCODE_DATA_DIR`; `XDG_DATA_HOME` (append `opencode`) |
| Hermes | `HERMES_HOME` |
| Grok | `GROK_HOME` (append `sessions`) |
| Pi | `PI_CODING_AGENT_DIR` (append `sessions`); `PI_CODING_AGENT_SESSION_DIR` |
| omp | `OMP_CODING_AGENT_DIR` (append `sessions`) |
| Kiro | `KIRO_HOME` (append `sessions`); `KIRO_DATA_DIR` |

Direct roots can also be supplied as semicolon-separated Windows paths in
`PTB_CLAUDE_CODE_ROOTS`, `PTB_OPENCODE_ROOTS`, `PTB_HERMES_ROOTS`,
`PTB_GROK_ROOTS`, `PTB_PI_ROOTS`, `PTB_OMP_ROOTS`, or `PTB_KIRO_ROOTS`.
These supplement defaults. They may point to accessible WSL/exported records;
WSL distributions are not automatically searched. Parser support does not imply
that each upstream CLI has native Windows support.

## Reading and validation

Files are read with sharing enabled. Unchanged non-Kiro files reuse parsed token
records; SQLite is reopened read-only each refresh so WAL updates are visible.
Database access/schema failures are surfaced to the usage store, which preserves
the last successful provider snapshot and displays an error. No database copies
or conversation exports are created. Kiro reads text transiently for estimation;
the other adapters aggregate token metadata.

Release build and 149 tests passed on 2026-09-08, including synthetic fixtures for
all seven new adapters. The fixtures exercise deduplication, replay exclusions,
incremental record changes, partial JSONL and database errors. Live sessions for
these seven tools still need validation; supported source schemas can change.
Existing live Antigravity growth was separately user-verified on 2026-09-07.
