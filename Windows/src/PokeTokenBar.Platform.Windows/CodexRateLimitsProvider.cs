using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using PokeTokenBar.Core;

namespace PokeTokenBar.Platform.Windows;

public sealed class CodexRateLimitsProvider : IRateLimitProvider
{
    private const string ProviderVersion = "0.4.0";

    public string Id => "codex";

    public string DisplayName => "Codex";

    public async Task<ProviderRateLimitSnapshot?> FetchAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var binary = ResolveBinary();
        if (binary is null)
        {
            return null;
        }

        var result = await RunAppServerAsync(binary, cancellationToken).ConfigureAwait(false);
        return CodexRateLimitParser.Parse(result, now);
    }

    internal static string? ResolveBinary(string? userProfile = null)
    {
        var configured = Environment.GetEnvironmentVariable("PTB_CODEX_BINARY");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var home = userProfile
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, ".codex", "bin", "codex.exe"),
            Path.Combine(home, ".codex", "bin", "codex.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Codex", "codex.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "codex", "codex.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm", "codex.cmd"),
        };
        var installed = candidates.FirstOrDefault(File.Exists);
        if (installed is not null)
        {
            return installed;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in new[] { "codex.exe", "codex.cmd", "codex.bat" })
            {
                var candidate = Path.Combine(directory.Trim(), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static async Task<JsonElement> RunAppServerAsync(
        string binary,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(binary),
            EnableRaisingEvents = true,
        };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Codex 실행에 실패했습니다: {binary}");
        }

        try
        {
            var initialize = JsonSerializer.Serialize(new
            {
                method = "initialize",
                id = 0,
                @params = new
                {
                    clientInfo = new
                    {
                        name = "poke_token_bar_windows",
                        title = "PokeTokenBar",
                        version = ProviderVersion,
                    },
                    capabilities = new { experimentalApi = true },
                },
            });
            var initialized = "{\"method\":\"initialized\",\"params\":{}}";
            var request = "{\"method\":\"account/rateLimits/read\",\"id\":1,\"params\":{}}";
            await process.StandardInput.WriteLineAsync(initialize).ConfigureAwait(false);
            await process.StandardInput.WriteLineAsync(initialized).ConfigureAwait(false);
            await process.StandardInput.WriteLineAsync(request).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            while (await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false)
                   is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var id)
                    || id.ValueKind != JsonValueKind.Number
                    || id.GetInt32() != 1)
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    throw new InvalidOperationException(
                        $"Codex rate limit 요청 실패: {error}");
                }

                if (!root.TryGetProperty("result", out var result))
                {
                    throw new InvalidOperationException("Codex rate limit 응답에 result가 없습니다.");
                }

                return result.Clone();
            }

            throw new InvalidOperationException("Codex rate limit 응답을 받지 못했습니다.");
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(string binary)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (binary.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add($"\"{binary}\" app-server --stdio");
        }
        else
        {
            startInfo.FileName = binary;
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");
        }

        return startInfo;
    }
}

public static class CodexRateLimitParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ProviderRateLimitSnapshot? Parse(
        string json,
        DateTimeOffset fetchedAt)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement, fetchedAt);
    }

    public static ProviderRateLimitSnapshot? Parse(
        JsonElement result,
        DateTimeOffset fetchedAt)
    {
        var response = result.Deserialize<CodexRateLimitResponse>(JsonOptions)
            ?? throw new JsonException("Codex rate limit 응답을 해석할 수 없습니다.");
        if (response.RateLimits is null)
        {
            return null;
        }

        var snapshots = new List<CodexRateLimitSnapshotPayload> { response.RateLimits };
        if (response.RateLimitsByLimitId is not null)
        {
            var primaryKey = response.RateLimits.LimitId ?? "codex";
            snapshots.AddRange(response.RateLimitsByLimitId
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Where(pair => pair.Key != primaryKey
                    && pair.Value.LimitId != response.RateLimits.LimitId)
                .Select(pair => pair.Value));
        }

        var windows = new List<RateLimitWindow>();
        var plans = new List<string>();
        foreach (var snapshot in snapshots)
        {
            var bucket = snapshot.LimitName ?? snapshot.LimitId ?? "codex";
            if (!string.IsNullOrWhiteSpace(snapshot.PlanType))
            {
                plans.Add(snapshot.PlanType!);
            }

            AddWindow(windows, bucket, "primary", snapshot.Primary);
            AddWindow(windows, bucket, "secondary", snapshot.Secondary);
            if (snapshot.IndividualLimit is not null)
            {
                var individual = snapshot.IndividualLimit;
                windows.Add(new RateLimitWindow(
                    $"{bucket}:individual",
                    "개별 지출 한도",
                    individual.UsedPercent,
                    FromUnixSeconds(individual.ResetsAt),
                    null));
            }
        }

        return new ProviderRateLimitSnapshot(
            "codex",
            "Codex",
            plans.FirstOrDefault(),
            windows,
            fetchedAt);
    }

    private static void AddWindow(
        ICollection<RateLimitWindow> windows,
        string bucket,
        string kind,
        CodexRateLimitWindowPayload? payload)
    {
        if (payload is null)
        {
            return;
        }

        var name = payload.WindowDurationMins switch
        {
            300 => "5시간 세션",
            10_080 => "주간",
            int minutes when minutes >= 60 && minutes % 60 == 0 => $"{minutes / 60}시간",
            int minutes => $"{minutes}분",
            _ => "한도",
        };
        windows.Add(new RateLimitWindow(
            $"{bucket}:{kind}",
            name,
            payload.UsedPercent,
            FromUnixSeconds(payload.ResetsAt),
            payload.WindowDurationMins));
    }

    private static DateTimeOffset? FromUnixSeconds(long? value) =>
        value is { } seconds
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    private sealed class CodexRateLimitResponse
    {
        [JsonPropertyName("rateLimits")]
        public CodexRateLimitSnapshotPayload? RateLimits { get; set; }

        [JsonPropertyName("rateLimitsByLimitId")]
        public Dictionary<string, CodexRateLimitSnapshotPayload>? RateLimitsByLimitId { get; set; }
    }

    private sealed class CodexRateLimitSnapshotPayload
    {
        public string? LimitId { get; set; }
        public string? LimitName { get; set; }
        public CodexRateLimitWindowPayload? Primary { get; set; }
        public CodexRateLimitWindowPayload? Secondary { get; set; }
        public CodexSpendControlLimitPayload? IndividualLimit { get; set; }
        public string? PlanType { get; set; }
    }

    private sealed class CodexRateLimitWindowPayload
    {
        public int UsedPercent { get; set; }
        public int? WindowDurationMins { get; set; }
        public long? ResetsAt { get; set; }
    }

    private sealed class CodexSpendControlLimitPayload
    {
        public int RemainingPercent { get; set; }
        public long ResetsAt { get; set; }

        public int UsedPercent => Math.Clamp(100 - RemainingPercent, 0, 100);
    }
}
