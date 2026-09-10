using PokeTokenBar.Core;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class HiggsfieldUsageTests
{
    [Fact]
    public void Parses_official_status_and_transaction_shapes()
    {
        var status = HiggsfieldUsage.ParseStatus(
            """{"credits":123.5,"email":"user@example.com","subscription_plan_type":"starter"}""");
        var page = HiggsfieldUsage.ParseTransactions(
            """
            {"cursor":"next-page","items":[
              {"id":"spend-1","at":"2026-09-10T10:00:00+09:00","model":"Soul","credits":-2.5,"action":"spend"},
              {"id":"grant-1","date":"2026-09-10T09:00:00+09:00","credits":270,"action":"grant"}
            ]}
            """);

        Assert.Equal(123.5m, status.Credits);
        Assert.Equal("starter", status.Plan);
        Assert.Equal("next-page", page.Cursor);
        var transaction = Assert.Single(page.Items);
        Assert.Equal("spend-1", transaction.Id);
        Assert.Equal(-2.5m, transaction.Credits);
        Assert.Equal("Soul", transaction.Model);
    }

    [Fact]
    public void Refunds_create_debt_before_later_spend_can_grow_again()
    {
        var transactions = new[]
        {
            Transaction("1", 1, 10, "spend", -10),
            Transaction("2", 2, 10, "refund", 4),
            Transaction("3", 2, 11, "spend", -2),
            Transaction("4", 3, 10, "spend", -3),
        };

        var earned = HiggsfieldUsage.GrowthEligibleCreditsByDay(transactions);

        Assert.Equal(10m, earned[new DateOnly(2026, 9, 1)]);
        Assert.False(earned.ContainsKey(new DateOnly(2026, 9, 2)));
        Assert.Equal(1m, earned[new DateOnly(2026, 9, 3)]);
    }

    [Fact]
    public void Keeps_identical_anonymous_transactions_as_separate_spend()
    {
        const string json = """
            {"cursor":null,"items":[
              {"at":"2026-09-10T10:00:00Z","model":"Soul","credits":-1,"action":"spend"},
              {"at":"2026-09-10T10:00:00Z","model":"Soul","credits":-1,"action":"spend"}
            ]}
            """;

        var page = HiggsfieldUsage.ParseTransactions(json);

        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, transaction => Assert.Null(transaction.Id));
        Assert.Equal(2m, HiggsfieldUsage.GrowthEligibleCreditsByDay(page.Items).Values.Sum());
    }

    [Fact]
    public void Snapshot_keeps_credits_and_converted_growth_separate()
    {
        var now = LocalInstant(2026, 9, 3, 14);
        var snapshot = HiggsfieldUsage.CreateSnapshot(
            [Transaction("1", 1, 10, "spend", -10), Transaction("2", 3, 10, "spend", -1.5m)],
            new HiggsfieldAccountStatus(258.5m, null, "starter"),
            now,
            DayOfWeek.Monday);

        Assert.NotNull(snapshot);
        Assert.Equal(975_000, snapshot.TodayTotalTokens);
        Assert.Equal(7_475_000, snapshot.WeekTotal!.TotalTokens);
        Assert.Equal(11.5m, snapshot.NativeUsage!.Week);
        Assert.Equal(258.5m, snapshot.NativeUsage.Balance);
        Assert.Equal(650_000, snapshot.NativeUsage.GrowthTokensPerUnit);
    }

    [Fact]
    public async Task Provider_follows_cursors_and_deduplicates_transactions()
    {
        var now = LocalInstant(2026, 9, 10, 14);
        var timestamp = LocalInstant(2026, 9, 10, 10).ToString("O");
        var client = new FakeClient(
            """{"credits":268,"subscription_plan_type":"starter"}""",
            $$"""{"cursor":"page-2","items":[{"id":"one","date":"{{timestamp}}","credits":-2,"action":"spend"}]}""",
            $$"""{"cursor":null,"items":[{"id":"one","date":"{{timestamp}}","credits":-2,"action":"spend"}]}""");

        var snapshot = await new HiggsfieldUsageProvider(client).FetchAsync(now);

        Assert.NotNull(snapshot);
        Assert.Equal(1_300_000, snapshot.TodayTotalTokens);
        Assert.Contains(client.Calls, call => call.Contains("--cursor") && call.Contains("page-2"));
    }

    [Theory]
    [InlineData("0.25", 162_500)]
    [InlineData("1", 650_000)]
    public void Converts_fractional_credits_without_rounding_up(string credits, long expected)
    {
        Assert.Equal(expected, HiggsfieldUsage.ConvertToGrowthTokens(decimal.Parse(credits)));
    }

    private static HiggsfieldTransaction Transaction(
        string id,
        int day,
        int hour,
        string action,
        decimal credits) => new(
        id,
        LocalInstant(2026, 9, day, hour),
        credits,
        action,
        "Soul");

    private static DateTimeOffset LocalInstant(int year, int month, int day, int hour)
    {
        var local = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private sealed class FakeClient(string status, string firstPage, string secondPage)
        : IHiggsfieldCliClient
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<string> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Calls.Add(arguments.ToArray());
            if (arguments.Contains("status"))
            {
                return Task.FromResult(status);
            }

            return Task.FromResult(arguments.Contains("--cursor") ? secondPage : firstPage);
        }
    }
}
