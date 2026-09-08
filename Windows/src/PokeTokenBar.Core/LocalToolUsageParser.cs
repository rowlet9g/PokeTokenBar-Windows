using System.Globalization;
using System.Text.Json;

namespace PokeTokenBar.Core;

// Format contracts adapted from chattymin/PokeTokenBar (MIT); see docs/reference/local-providers.md.
public static class LocalToolUsageParser
{
    public static UsageEntry? Parse(string provider, JsonElement record, string fallbackId)
    {
        return provider switch
        {
            "claude_code" => Claude(record, fallbackId),
            "grok" => Grok(record),
            "pi" or "omp" => Pi(provider, record, fallbackId),
            "opencode" => OpenCode(record, fallbackId),
            _ => null,
        };
    }

    public static JsonElement Field(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) ? field : default;

    public static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static string? Text(JsonElement value, string name) => Text(Field(value, name));

    public static long? Number(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return Math.Max(0, number);
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out number)) return Math.Max(0, number);
        return null;
    }

    public static long Count(JsonElement value, string name) => Number(Field(value, name)) ?? 0;

    public static DateTimeOffset? Date(JsonElement value, bool milliseconds = false)
    {
        double number = 0;
        var numeric = value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number);
        if (!numeric && !double.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return DateTimeOffset.TryParse(Text(value), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
        if (!double.IsFinite(number) || number <= 0) return null;
        try
        {
            return DateTimeOffset.UnixEpoch.AddMilliseconds(milliseconds || number >= 100_000_000_000 ? number : number * 1000);
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public static UsageEntry? Entry(string id, DateTimeOffset? date, string? model,
        long input, long output, long cacheWrite = 0, long cacheRead = 0, long total = 0)
    {
        if (date is null) return null;
        var sum = UsageMath.SaturatingSum(input, output, cacheWrite, cacheRead);
        if (total > sum) output = UsageMath.SaturatingAdd(output, total - sum);
        if (UsageMath.SaturatingSum(input, output, cacheWrite, cacheRead) == 0) return null;
        return new UsageEntry(id, date.Value, DateOnly.FromDateTime(date.Value.LocalDateTime), model ?? "unknown",
            input, output, cacheWrite, cacheRead);
    }

    private static UsageEntry? Claude(JsonElement record, string fallbackId)
    {
        if (Text(record, "type") != "assistant") return null;
        var message = Field(record, "message");
        var usage = Field(message, "usage");
        var messageId = Text(message, "id");
        var id = string.IsNullOrWhiteSpace(messageId) ? fallbackId : messageId + "|" + Text(record, "requestId");
        return Entry("claude_code|" + id, Date(Field(record, "timestamp")), Text(message, "model"),
            Count(usage, "input_tokens"), Count(usage, "output_tokens"),
            Count(usage, "cache_creation_input_tokens"), Count(usage, "cache_read_input_tokens"));
    }

    private static UsageEntry? Pi(string provider, JsonElement record, string fallbackId)
    {
        var id = Text(record, "id");
        if (provider == "omp") id = fallbackId.Split('|')[0] + "|" + (id ?? fallbackId);
        if (string.IsNullOrWhiteSpace(id)) return null;
        var type = Text(record, "type");
        var message = Field(record, "message");
        JsonElement usage;
        DateTimeOffset? date;
        if (type == "message")
        {
            if (provider == "omp" && Text(message, "role") != "assistant") return null;
            if (Text(message, "stopReason") is "aborted" or "error") return null;
            usage = Field(message, "usage");
            date = Date(Field(message, "timestamp"), milliseconds: true) ?? Date(Field(record, "timestamp"));
        }
        else if (type is "compaction" or "branch_summary")
        {
            usage = Field(record, "usage");
            date = Date(Field(record, "timestamp"));
        }
        else return null;
        var granular = new[] { "input", "output", "cacheWrite", "cacheRead" }
            .Any(name => Number(Field(usage, name)) is not null);
        return Entry(provider + "|" + id, date, Text(record, "model") ?? Text(message, "model") ?? provider,
            granular ? Count(usage, "input") : Count(usage, "totalTokens"), Count(usage, "output"),
            Count(usage, "cacheWrite"), Count(usage, "cacheRead"));
    }

    private static UsageEntry? OpenCode(JsonElement record, string fallbackId)
    {
        if (Text(record, "modelID") is not { } model || Text(record, "providerID") is null) return null;
        var usage = Field(record, "tokens");
        var cache = Field(usage, "cache");
        return Entry("opencode|" + (Text(record, "id") ?? fallbackId),
            Date(Field(Field(record, "time"), "created")), model,
            Count(usage, "input"), Count(usage, "output"), Count(cache, "write"), Count(cache, "read"), Count(usage, "total"));
    }

    private static UsageEntry? Grok(JsonElement record)
    {
        var notification = Field(record, "params");
        if (notification.ValueKind != JsonValueKind.Object) notification = record;
        var update = Field(notification, "update");
        var meta = Field(notification, "_meta");
        if (Text(update, "sessionUpdate") != "turn_completed" || Field(meta, "isReplay").ValueKind == JsonValueKind.True
            || Text(update, "prompt_id") is not { Length: > 0 } id) return null;
        var usage = Field(update, "usage");
        var cache = Number(Field(usage, "cachedReadTokens")) ?? Count(usage, "cached_read_tokens");
        long input;
        if (Number(Field(usage, "inputTokens")) is { } full)
        {
            cache = Math.Min(cache, full);
            input = full - cache;
        }
        else input = Count(usage, "input_tokens");
        return Entry("grok|" + id,
            Date(Field(meta, "agentTimestampMs"), milliseconds: true) ?? Date(Field(record, "timestamp")), "grok",
            input, Number(Field(usage, "outputTokens")) ?? Count(usage, "output_tokens"), 0, cache,
            Number(Field(usage, "totalTokens")) ?? Count(usage, "total_tokens"));
    }
}
