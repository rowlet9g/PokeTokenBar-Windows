using System.Globalization;
using System.Text.Json;

namespace PokeTokenBar.Core;

public sealed record HiggsfieldAccountStatus(decimal Credits, string? Email, string? Plan);

public sealed record HiggsfieldTransaction(
    string? Id,
    DateTimeOffset Timestamp,
    decimal Credits,
    string Action,
    string Model);

public sealed record HiggsfieldTransactionPage(
    IReadOnlyList<HiggsfieldTransaction> Items,
    string? Cursor);

public static class HiggsfieldUsage
{
    public const long GrowthTokensPerCredit = 650_000;

    public static HiggsfieldAccountStatus ParseStatus(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !TryDecimal(root, out var credits, "credits"))
        {
            throw new InvalidDataException("Higgsfield account status did not contain a credit balance.");
        }

        return new HiggsfieldAccountStatus(
            credits,
            Text(root, "email"),
            Text(root, "subscription_plan_type", "subscriptionPlanType", "plan"));
    }

    public static HiggsfieldTransactionPage ParseTransactions(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var items = root.ValueKind == JsonValueKind.Array
            ? root
            : Property(root, "items") is { ValueKind: JsonValueKind.Array } value
                ? value
                : throw new InvalidDataException("Higgsfield transaction response did not contain an items array.");
        var result = new List<HiggsfieldTransaction>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var action = Text(item, "action", "type")?.Trim().ToLowerInvariant();
            if (action is not ("spend" or "refund"))
            {
                continue;
            }

            if (!TryDecimal(item, out var credits, "credits", "credit_amount", "creditAmount", "amount")
                || !TryTimestamp(item, out var timestamp))
            {
                throw new InvalidDataException("A Higgsfield spend/refund transaction had an unsupported schema.");
            }

            result.Add(new HiggsfieldTransaction(
                Text(item, "id", "transaction_id", "transactionId"),
                timestamp,
                credits,
                action,
                Text(item, "model", "model_name", "modelName") ?? "Higgsfield"));
        }

        return new HiggsfieldTransactionPage(
            result,
            root.ValueKind == JsonValueKind.Object
                ? Text(root, "cursor", "next_cursor", "nextCursor")
                : null);
    }

    public static ProviderSnapshot? CreateSnapshot(
        IEnumerable<HiggsfieldTransaction> transactions,
        HiggsfieldAccountStatus status,
        DateTimeOffset now,
        DayOfWeek firstDayOfWeek)
    {
        var earnedByDay = GrowthEligibleCreditsByDay(transactions);
        var localDay = DateOnly.FromDateTime(now.LocalDateTime);
        var weekStart = UsageAggregation.StartOfWeek(localDay, firstDayOfWeek);
        var monthStart = new DateOnly(localDay.Year, localDay.Month, 1);
        var todayCredits = Credits(earnedByDay, localDay, localDay);
        var weekCredits = Credits(earnedByDay, weekStart, localDay);
        var monthCredits = Credits(earnedByDay, monthStart, localDay);
        if (todayCredits == 0 && weekCredits == 0 && monthCredits == 0)
        {
            return null;
        }

        var todayTokens = ConvertToGrowthTokens(todayCredits);
        var weekTokens = ConvertToGrowthTokens(weekCredits);
        var monthTokens = ConvertToGrowthTokens(monthCredits);
        return new ProviderSnapshot(
            "higgsfield",
            "Higgsfield",
            todayTokens == 0 ? null : new DailyUsage(
                localDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                todayTokens, 0, 0, 0, todayTokens, 0),
            null,
            new PeriodUsage(
                weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                weekTokens,
                0),
            new PeriodUsage(UsageAggregation.MonthKey(localDay), monthTokens, 0),
            now,
            ReportsCost: false,
            new NativeUsage(
                "credit",
                todayCredits,
                weekCredits,
                monthCredits,
                status.Credits,
                status.Plan,
                GrowthTokensPerCredit));
    }

    public static IReadOnlyDictionary<DateOnly, decimal> GrowthEligibleCreditsByDay(
        IEnumerable<HiggsfieldTransaction> transactions)
    {
        var earned = new Dictionary<DateOnly, decimal>();
        decimal netSpent = 0;
        decimal highWater = 0;
        foreach (var transaction in transactions
                     .OrderBy(item => item.Timestamp)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            var magnitude = Math.Abs(transaction.Credits);
            netSpent += string.Equals(transaction.Action, "refund", StringComparison.OrdinalIgnoreCase)
                ? -magnitude
                : magnitude;
            if (netSpent <= highWater)
            {
                continue;
            }

            var increment = netSpent - highWater;
            highWater = netSpent;
            var day = DateOnly.FromDateTime(transaction.Timestamp.LocalDateTime);
            earned[day] = earned.GetValueOrDefault(day) + increment;
        }

        return earned;
    }

    public static long ConvertToGrowthTokens(decimal credits)
    {
        if (credits <= 0)
        {
            return 0;
        }

        var converted = decimal.Truncate(credits * GrowthTokensPerCredit);
        return converted >= long.MaxValue ? long.MaxValue : (long)converted;
    }

    private static decimal Credits(
        IReadOnlyDictionary<DateOnly, decimal> values,
        DateOnly from,
        DateOnly to) => values
        .Where(pair => pair.Key >= from && pair.Key <= to)
        .Sum(pair => pair.Value);

    private static bool TryTimestamp(JsonElement element, out DateTimeOffset value)
    {
        foreach (var name in new[] { "at", "date", "created_at", "createdAt", "timestamp" })
        {
            if (Property(element, name) is not { } property)
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(
                    property.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out value))
            {
                return true;
            }

            if (property.ValueKind == JsonValueKind.Number
                && property.TryGetInt64(out var unix))
            {
                value = unix > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                    : DateTimeOffset.FromUnixTimeSeconds(unix);
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryDecimal(JsonElement element, out decimal value, params string[] names)
    {
        foreach (var name in names)
        {
            if (Property(element, name) is not { } property)
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value))
            {
                return true;
            }

            if (property.ValueKind == JsonValueKind.String
                && decimal.TryParse(
                    property.GetString(),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static string? Text(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (Property(element, name) is { ValueKind: JsonValueKind.String } property)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}
