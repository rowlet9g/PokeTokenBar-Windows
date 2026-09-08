using System.Text;
using System.Text.Json;
using PokeTokenBar.Core;
using static PokeTokenBar.Core.LocalToolUsageParser;

namespace PokeTokenBar.Platform.Windows;

/// <summary>Kiro's upstream-compatible approximation: UTF-8 text bytes / 4, including resent history.</summary>
public static class KiroUsageParser
{
    public static IReadOnlyList<UsageEntry> ParseConversation(JsonElement conversation, string fallbackId)
    {
        List<UsageEntry> entries = [];
        var id = Text(conversation, "conversation_id") ?? fallbackId;
        var history = Field(conversation, "history");
        if (history.ValueKind != JsonValueKind.Array) return entries;
        long accumulated = Bytes(Field(conversation, "latest_summary"));
        foreach (var turn in history.EnumerateArray())
        {
            var user = TurnBytes(Field(turn, "user"));
            var meta = Field(turn, "request_metadata");
            var rawTime = Field(meta, "request_start_timestamp_ms");
            var date = Date(rawTime, milliseconds: true);
            if (Entry($"kiro|{id}|{date?.ToUnixTimeMilliseconds()}", date, Text(meta, "model_id"),
                UsageMath.SaturatingAdd(accumulated, user) / 4, Count(meta, "response_size") / 4) is { } entry) entries.Add(entry);
            accumulated = UsageMath.SaturatingSum(accumulated, user, TurnBytes(Field(turn, "assistant")));
        }
        return entries;
    }

    public static IReadOnlyList<UsageEntry> ParseFile(string file, CancellationToken cancellationToken = default)
    {
        var v3 = Path.GetFileName(file) == "messages.jsonl";
        var companion = v3 ? Path.Combine(Path.GetDirectoryName(file)!, "session.json") : Path.ChangeExtension(file, ".json");
        using var metadata = File.Exists(companion) ? LocalToolUsageReader.ReadJson(companion) : JsonDocument.Parse("{}");
        var session = metadata.RootElement;
        var id = Text(session, v3 ? "id" : "session_id")
            ?? (v3 ? Path.GetFileName(Path.GetDirectoryName(file)) : Path.GetFileNameWithoutExtension(file));
        var model = v3 ? Text(session, "modelId")
            : Text(Field(Field(Field(session, "session_state"), "rts_model_state"), "model_info"), "model_id");
        var fallbackDate = Date(Field(session, "createdAt")) ?? Date(Field(session, "lastModifiedAt"));
        List<UsageEntry> entries = [];
        long history = 0, prompt = 0, output = 0, tools = 0;
        var started = false;
        var index = 0;
        DateTimeOffset? date = null;

        void Flush()
        {
            if (started && UsageMath.SaturatingSum(prompt, output, tools) > 0)
            {
                var timestamp = date ?? (v3 ? fallbackDate : null);
                var key = v3 ? $"kiro|v3|{id}|{index}" : $"kiro|cli|{id}|{timestamp?.ToUnixTimeMilliseconds()}";
                if (Entry(key, timestamp, model, UsageMath.SaturatingSum(history, prompt, tools) / 4, output / 4) is { } entry)
                    entries.Add(entry);
                index++;
            }
            history = UsageMath.SaturatingSum(history, prompt, output, tools);
            prompt = output = tools = 0;
            date = null;
            started = false;
        }

        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (document)
            {
                var record = document.RootElement;
                if (!v3)
                {
                    var data = Field(record, "data");
                    switch (Text(record, "kind"))
                    {
                        case "Prompt":
                            Flush(); started = true; prompt = TextBytes(Field(data, "content"));
                            date = Date(Field(Field(data, "meta"), "timestamp")); break;
                        case "AssistantMessage": output = UsageMath.SaturatingAdd(output, TextBytes(Field(data, "content"))); break;
                        case "ToolResults": tools = UsageMath.SaturatingAdd(tools, TextBytes(Field(data, "content"))); break;
                        case "Clear": Flush(); history = 0; break;
                    }
                    continue;
                }
                var payload = Field(record, "payload");
                var structured = payload.ValueKind == JsonValueKind.Object;
                if (!structured) payload = record;
                var type = Text(payload, structured ? "type" : "role");
                var eventDate = Date(Field(record, "timestamp"));
                switch (type)
                {
                    case "user": case "human": case "prompt":
                        Flush(); started = true; prompt = TextBytes(Field(payload, "content")); date = eventDate; break;
                    case "assistant": case "bot":
                        output = UsageMath.SaturatingAdd(output, TextBytes(Field(payload, "content")));
                        started = started || output > 0; date ??= eventDate; break;
                    case "tool_call":
                        output = UsageMath.SaturatingAdd(output, Bytes(Field(payload, "args")));
                        started = started || output > 0; break;
                    case "tool_result": prompt = UsageMath.SaturatingAdd(prompt, TextBytes(Field(payload, "content"))); break;
                    case "turn_end": date ??= eventDate; Flush(); break;
                    // usage_summary contains credits, not API token counts or dollar costs.
                }
            }
        }
        Flush();
        return entries;
    }

    private static long TurnBytes(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? value.EnumerateObject().Where(property => property.Name != "images")
            .Aggregate(0L, (sum, property) => UsageMath.SaturatingAdd(sum, Bytes(property.Value)))
        : value.ValueKind == JsonValueKind.String ? Bytes(value) : 0;

    private static long TextBytes(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return Bytes(value);
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Aggregate(0L, (sum, item) => UsageMath.SaturatingAdd(sum, TextBytes(item)));
        if (value.ValueKind != JsonValueKind.Object) return 0;
        if (Text(value, "kind") is { } kind) return kind == "text" ? TextBytes(Field(value, "data")) : 0;
        foreach (var key in new[] { "content", "text", "data" })
            if (Field(value, key).ValueKind != JsonValueKind.Undefined) return TextBytes(Field(value, key));
        return 0;
    }

    private static long Bytes(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => Encoding.UTF8.GetByteCount(value.GetString()!),
            JsonValueKind.Array => value.EnumerateArray().Aggregate(0L, (sum, item) => UsageMath.SaturatingAdd(sum, Bytes(item))),
            JsonValueKind.Object => value.EnumerateObject()
                .Aggregate(0L, (sum, property) => UsageMath.SaturatingAdd(sum, Bytes(property.Value))),
            JsonValueKind.Number => Encoding.UTF8.GetByteCount(value.GetRawText()),
            _ => 0,
        };
    }
}
