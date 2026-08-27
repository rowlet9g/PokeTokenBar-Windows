using System.Text;
using System.Text.Json;

namespace PokeTokenBar.Core.Tests;

public sealed class CodexUsageReaderTests
{
    private const string ParentId = "00000000-0000-7000-8000-000000000001";
    private const string ChildId = "00000000-0000-7000-8000-000000000002";
    private const string SiblingId = "00000000-0000-7000-8000-000000000004";

    [Fact]
    public void Fork_fixtures_trim_replay_and_keep_each_childs_own_usage()
    {
        var entries = new CodexUsageReader().ReadEntries(
            [FixtureDirectory("CodexFork")],
            DateTimeOffset.MinValue);

        Assert.Equal(369_215, entries.Sum(entry => entry.Total));
        Assert.Equal(312_814, entries.Where(entry => entry.Id.StartsWith($"codex|{ParentId}|", StringComparison.Ordinal)).Sum(entry => entry.Total));
        Assert.Equal(28_138, entries.Where(entry => entry.Id.StartsWith($"codex|{ChildId}|", StringComparison.Ordinal)).Sum(entry => entry.Total));
        Assert.Equal(28_263, entries.Where(entry => entry.Id.StartsWith($"codex|{SiblingId}|", StringComparison.Ordinal)).Sum(entry => entry.Total));
        Assert.Equal(entries.Count, entries.Select(entry => entry.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("parent.jsonl", "child.jsonl", ChildId, 115_607, 46_353)]
    [InlineData("parent-v145.jsonl", "child-v145.jsonl", "00000000-0000-7000-8000-000000000146", 106_583, 43_180)]
    public void Subagent_fixtures_keep_all_child_usage_without_replay_trimming(
        string parent,
        string child,
        string childId,
        long combined,
        long childTotal)
    {
        using var temporary = TemporaryDirectory.Create();
        File.Copy(Path.Combine(FixtureDirectory("CodexSubagent"), parent), Path.Combine(temporary.Path, parent));
        File.Copy(Path.Combine(FixtureDirectory("CodexSubagent"), child), Path.Combine(temporary.Path, child));

        var entries = new CodexUsageReader().ReadEntries([temporary.Path], DateTimeOffset.MinValue);

        Assert.Equal(combined, entries.Sum(entry => entry.Total));
        Assert.Equal(childTotal, entries
            .Where(entry => entry.Id.StartsWith($"codex|{childId}|", StringComparison.Ordinal))
            .Sum(entry => entry.Total));
    }

    [Fact]
    public void Subagent_child_keeps_its_first_turn_when_parent_file_is_missing()
    {
        using var temporary = TemporaryDirectory.Create();
        File.Copy(
            Path.Combine(FixtureDirectory("CodexSubagent"), "child.jsonl"),
            Path.Combine(temporary.Path, "child.jsonl"));

        var entries = new CodexUsageReader().ReadEntries([temporary.Path], DateTimeOffset.MinValue);

        Assert.Equal([23_062L, 23_291L], entries.Select(entry => entry.Total));
        Assert.All(entries, entry => Assert.StartsWith($"codex|{ChildId}|", entry.Id));
    }

    [Fact]
    public void Consecutive_identical_usage_states_are_counted_once()
    {
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "rollout-duplicate.jsonl");
        File.WriteAllLines(path,
        [
            SessionMeta("session-a", null, "user"),
            TokenCount("2026-08-27T01:00:00.000Z", 100, 0, 10, 100, 0, 10),
            TokenCount("2026-08-27T01:00:01.000Z", 100, 0, 10, 100, 0, 10),
        ]);

        var entries = new CodexUsageReader().ReadEntries([temporary.Path], DateTimeOffset.MinValue);

        var entry = Assert.Single(entries);
        Assert.Equal(110, entry.Total);
    }

    [Fact]
    public void Cumulative_reset_starts_a_new_canonical_epoch()
    {
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "rollout-reset.jsonl");
        File.WriteAllLines(path,
        [
            SessionMeta("session-a", null, "user"),
            TokenCount("2026-08-27T01:00:00.000Z", 100, 0, 10, 100, 0, 10),
            TokenCount("2026-08-27T01:01:00.000Z", 50, 0, 5, 50, 0, 5),
        ]);

        var entries = new CodexUsageReader().ReadEntries([temporary.Path], DateTimeOffset.MinValue);

        Assert.Equal(2, entries.Count);
        Assert.StartsWith("codex|session-a|0|", entries[0].Id);
        Assert.StartsWith("codex|session-a|1|", entries[1].Id);
    }

    [Fact]
    public void Probe_reads_metadata_lines_larger_than_one_chunk()
    {
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "rollout-large-meta.jsonl");
        var padding = new string('x', 200_000);
        File.WriteAllText(
            path,
            $"{{\"type\":\"session_meta\",\"payload\":{{\"padding\":\"{padding}\",\"id\":\"large-session\"}}}}\n",
            new UTF8Encoding(false));

        var sessionId = new CodexUsageReader().ProbeSessionId(path);

        Assert.Equal("large-session", sessionId);
    }

    [Fact]
    public void Absurd_numeric_values_are_clamped_instead_of_overflowing()
    {
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "rollout-clamp.jsonl");
        File.WriteAllLines(path,
        [
            SessionMeta("session-a", null, "user"),
            """{"timestamp":"2026-08-27T01:00:00.000Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1e30,"cached_input_tokens":0,"output_tokens":1,"total_tokens":1e30},"last_token_usage":{"input_tokens":1e30,"cached_input_tokens":0,"output_tokens":1,"total_tokens":1e30}}}}""",
        ]);

        var entry = Assert.Single(new CodexUsageReader().ReadEntries([temporary.Path], DateTimeOffset.MinValue));

        Assert.Equal(CodexUsageReader.MaxParsedTokenValue, entry.Input);
        Assert.Equal(CodexUsageReader.MaxParsedTokenValue + 1, entry.Total);
    }

    [Fact]
    public void Parent_outside_the_scan_window_is_loaded_only_as_a_replay_dependency()
    {
        using var temporary = TemporaryDirectory.Create();
        var parent = Path.Combine(temporary.Path, "parent.jsonl");
        var child = Path.Combine(temporary.Path, "child.jsonl");
        File.Copy(Path.Combine(FixtureDirectory("CodexFork"), "parent.jsonl"), parent);
        File.Copy(Path.Combine(FixtureDirectory("CodexFork"), "child.jsonl"), child);
        File.SetLastWriteTimeUtc(parent, DateTime.UtcNow.AddDays(-10));
        File.SetLastWriteTimeUtc(child, DateTime.UtcNow);

        var entries = new CodexUsageReader().ReadEntries(
            [temporary.Path],
            DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Equal(28_138, entries.Sum(entry => entry.Total));
        Assert.All(entries, entry => Assert.StartsWith($"codex|{ChildId}|", entry.Id));
    }

    [Fact]
    public async Task Provider_builds_today_week_month_and_active_block_from_a_current_rollout()
    {
        using var temporary = TemporaryDirectory.Create();
        var now = DateTimeOffset.Now;
        var path = Path.Combine(temporary.Path, "rollout-current.jsonl");
        File.WriteAllLines(path,
        [
            SessionMeta("session-current", null, "user"),
            TokenCount(now.ToUniversalTime().ToString("O"), 100, 0, 10, 100, 0, 10),
        ]);
        var provider = new CodexUsageProvider([temporary.Path]);

        var snapshot = await provider.FetchAsync(now);

        Assert.NotNull(snapshot);
        Assert.Equal(110, snapshot.Today?.TotalTokens);
        Assert.Equal(110, snapshot.WeekTotal?.TotalTokens);
        Assert.Equal(110, snapshot.MonthTotal?.TotalTokens);
        Assert.Equal(110, snapshot.ActiveBlock?.TotalTokens);
    }

    private static string FixtureDirectory(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static string SessionMeta(string id, string? parentId, string threadSource)
    {
        var parent = parentId is null ? string.Empty : $",\"forked_from_id\":\"{parentId}\"";
        return $"{{\"type\":\"session_meta\",\"timestamp\":\"2026-08-27T00:00:00.000Z\",\"payload\":{{\"id\":\"{id}\",\"session_id\":\"{id}\",\"thread_source\":\"{threadSource}\"{parent}}}}}";
    }

    private static string TokenCount(
        string timestamp,
        long cumulativeInput,
        long cumulativeCached,
        long cumulativeOutput,
        long lastInput,
        long lastCached,
        long lastOutput) =>
        JsonSerializer.Serialize(new
        {
            timestamp,
            type = "event_msg",
            payload = new
            {
                type = "token_count",
                info = new
                {
                    total_token_usage = new
                    {
                        input_tokens = cumulativeInput,
                        cached_input_tokens = cumulativeCached,
                        output_tokens = cumulativeOutput,
                        reasoning_output_tokens = 0,
                        total_tokens = cumulativeInput + cumulativeOutput,
                    },
                    last_token_usage = new
                    {
                        input_tokens = lastInput,
                        cached_input_tokens = lastCached,
                        output_tokens = lastOutput,
                        reasoning_output_tokens = 0,
                        total_tokens = lastInput + lastOutput,
                    },
                },
            },
        });

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PokeTokenBarTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
