using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PokeTokenBar.Core;

namespace PokeTokenBar.Platform.Windows;

/// <summary>
/// Reads the OAuth credential that Claude Code stores locally and queries the
/// official Claude Code usage endpoint. The access token never leaves memory and
/// is not included in errors, logs, or the returned snapshot.
/// </summary>
public sealed class ClaudeRateLimitsProvider : IRateLimitProvider
{
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    private const string OAuthBetaHeader = "oauth-2025-04-20";
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<string> _credentialPaths;

    public ClaudeRateLimitsProvider(
        HttpClient httpClient,
        IEnumerable<string>? credentialPaths = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _credentialPaths = (credentialPaths ?? ResolveCredentialPaths())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string Id => "claude_code";

    public string DisplayName => "Claude Code";

    public async Task<ProviderRateLimitSnapshot?> FetchAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var credential = await ReadCredentialAsync(cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBetaHeader);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var detail = response.StatusCode == HttpStatusCode.TooManyRequests
                ? "요청이 너무 많습니다"
                : response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "Claude Code 로그인 토큰이 만료되었거나 거부되었습니다"
                    : $"HTTP {status}";
            throw new InvalidOperationException($"Claude Code 공식 한도 요청 실패: {detail}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ClaudeRateLimitParser.Parse(
            json,
            now,
            credential.SubscriptionType,
            credential.RateLimitTier);
    }

    /// <summary>
    /// Returns only non-sensitive metadata so fixture tests can validate the
    /// credential shape without ever asserting or displaying an OAuth token.
    /// </summary>
    public static ClaudeCredentialMetadata ParseCredentialMetadata(string json)
    {
        var credential = ParseCredential(json);
        return credential is null
            ? new ClaudeCredentialMetadata(false, null, null, null)
            : new ClaudeCredentialMetadata(
                true,
                credential.SubscriptionType,
                credential.RateLimitTier,
                credential.ExpiresAt);
    }

    internal static IEnumerable<string> ResolveCredentialPaths(
        string? userProfile = null,
        Func<string, string?>? environment = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        environment ??= Environment.GetEnvironmentVariable;

        var paths = new List<string>();
        var configured = environment("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            // An explicit config directory selects an account, not an extra search root.
            return [Path.Combine(configured, ".credentials.json")];
        }

        paths.Add(Path.Combine(userProfile, ".claude", ".credentials.json"));
        paths.Add(Path.Combine(userProfile, ".config", "claude", ".credentials.json"));
        return paths;
    }

    private async Task<ClaudeCredential?> ReadCredentialAsync(CancellationToken cancellationToken)
    {
        foreach (var path in _credentialPaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            string json;
            try
            {
                json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var credential = ParseCredential(json);
            if (credential is not null)
            {
                return credential;
            }
        }

        return null;
    }

    private static ClaudeCredential? ParseCredential(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                || oauth.ValueKind != JsonValueKind.Object
                || !oauth.TryGetProperty("accessToken", out var tokenValue)
                || tokenValue.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var token = tokenValue.GetString();
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var expiresAt = ParseExpiresAt(oauth);
            if (expiresAt is not null && expiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return null;
            }

            return new ClaudeCredential(
                token,
                GetString(oauth, "subscriptionType"),
                GetString(oauth, "rateLimitTier"),
                expiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ParseExpiresAt(JsonElement oauth)
    {
        if (!oauth.TryGetProperty("expiresAt", out var value))
        {
            return null;
        }

        double? unixValue = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
        if (unixValue is not > 0)
        {
            return null;
        }

        var seconds = unixValue > 10_000_000_000 ? unixValue.Value / 1000 : unixValue.Value;
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record ClaudeCredential(
        string AccessToken,
        string? SubscriptionType,
        string? RateLimitTier,
        DateTimeOffset? ExpiresAt);
}

public sealed record ClaudeCredentialMetadata(
    bool HasAccessToken,
    string? SubscriptionType,
    string? RateLimitTier,
    DateTimeOffset? ExpiresAt);

public static class ClaudeRateLimitParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ProviderRateLimitSnapshot? Parse(
        string json,
        DateTimeOffset fetchedAt,
        string? subscriptionType = null,
        string? rateLimitTier = null)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement, fetchedAt, subscriptionType, rateLimitTier);
    }

    public static ProviderRateLimitSnapshot? Parse(
        JsonElement response,
        DateTimeOffset fetchedAt,
        string? subscriptionType = null,
        string? rateLimitTier = null)
    {
        var usage = response.Deserialize<ClaudeUsagePayload>(JsonOptions)
            ?? throw new JsonException("Claude Code 공식 한도 응답을 해석할 수 없습니다.");
        var windows = new List<RateLimitWindow>();

        AddLegacyWindow(windows, "five_hour", "5시간 세션", usage.FiveHour, 300);
        AddLegacyWindow(windows, "seven_day", "주간", usage.SevenDay, 10_080);
        AddLegacyWindow(windows, "seven_day_opus", "주간 (Opus)", usage.SevenDayOpus, 10_080);
        AddLegacyWindow(windows, "seven_day_sonnet", "주간 (Sonnet)", usage.SevenDaySonnet, 10_080);

        var hasLegacySession = usage.FiveHour is not null;
        var hasLegacyWeekly = usage.SevenDay is not null;
        var scopedIndex = 0;
        foreach (var entry in usage.Limits ?? [])
        {
            if (entry.IsActive == false || entry.Percent is null)
            {
                continue;
            }

            if (hasLegacySession && string.Equals(entry.Kind, "session", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (hasLegacyWeekly && string.Equals(entry.Kind, "weekly_all", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var id = $"limits:{entry.Kind ?? "unknown"}:{entry.Group ?? "all"}:{scopedIndex++}";
            var name = ScopedDisplayName(entry);
            windows.Add(new RateLimitWindow(
                id,
                name,
                entry.Percent.Value,
                ParseReset(entry.ResetsAt),
                WindowDuration(entry.Kind)));
        }

        if (windows.Count == 0)
        {
            return null;
        }

        return new ProviderRateLimitSnapshot(
            "claude_code",
            "Claude Code",
            PlanDisplay(subscriptionType, rateLimitTier),
            windows,
            fetchedAt);
    }

    private static void AddLegacyWindow(
        ICollection<RateLimitWindow> windows,
        string id,
        string displayName,
        ClaudeLimitWindowPayload? payload,
        int durationMinutes)
    {
        if (payload is null || payload.Utilization is null)
        {
            return;
        }

        windows.Add(new RateLimitWindow(
            $"claude:{id}",
            displayName,
            payload.Utilization.Value,
            ParseReset(payload.ResetsAt),
            durationMinutes));
    }

    private static string ScopedDisplayName(ClaudeLimitEntryPayload entry)
    {
        var model = entry.Scope?.Model?.DisplayName;
        if (!string.IsNullOrWhiteSpace(model))
        {
            return $"주간 ({model})";
        }

        return entry.Kind switch
        {
            "session" => "5시간 세션",
            "weekly_all" => "주간",
            "weekly_scoped" => "주간 (모델별)",
            _ when !string.IsNullOrWhiteSpace(entry.Group) => entry.Group!,
            _ => "한도",
        };
    }

    private static int? WindowDuration(string? kind) =>
        string.Equals(kind, "session", StringComparison.OrdinalIgnoreCase)
            ? 300
            : string.Equals(kind, "weekly_all", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "weekly_scoped", StringComparison.OrdinalIgnoreCase)
                    ? 10_080
                    : null;

    private static DateTimeOffset? ParseReset(string? raw) =>
        DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;

    private static string? PlanDisplay(string? subscriptionType, string? rateLimitTier)
    {
        var baseName = subscriptionType?.Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return null;
        }

        baseName = char.ToUpperInvariant(baseName[0]) + baseName[1..];
        var multiplier = Regex.Match(rateLimitTier ?? string.Empty, @"(?<!\d)(\d+x)(?!\w)", RegexOptions.IgnoreCase)
            .Groups[1].Value;
        return string.IsNullOrWhiteSpace(multiplier)
            ? baseName
            : $"{baseName} {multiplier}";
    }

    private sealed class ClaudeUsagePayload
    {
        [JsonPropertyName("five_hour")]
        public ClaudeLimitWindowPayload? FiveHour { get; set; }

        [JsonPropertyName("seven_day")]
        public ClaudeLimitWindowPayload? SevenDay { get; set; }

        [JsonPropertyName("seven_day_opus")]
        public ClaudeLimitWindowPayload? SevenDayOpus { get; set; }

        [JsonPropertyName("seven_day_sonnet")]
        public ClaudeLimitWindowPayload? SevenDaySonnet { get; set; }

        [JsonPropertyName("limits")]
        public List<ClaudeLimitEntryPayload>? Limits { get; set; }
    }

    private sealed class ClaudeLimitWindowPayload
    {
        [JsonPropertyName("utilization")]
        public double? Utilization { get; set; }

        [JsonPropertyName("resets_at")]
        public string? ResetsAt { get; set; }
    }

    private sealed class ClaudeLimitEntryPayload
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("group")]
        public string? Group { get; set; }

        [JsonPropertyName("percent")]
        public double? Percent { get; set; }

        [JsonPropertyName("resets_at")]
        public string? ResetsAt { get; set; }

        [JsonPropertyName("scope")]
        public ClaudeScopePayload? Scope { get; set; }

        [JsonPropertyName("is_active")]
        public bool? IsActive { get; set; }
    }

    private sealed class ClaudeScopePayload
    {
        [JsonPropertyName("model")]
        public ClaudeModelPayload? Model { get; set; }
    }

    private sealed class ClaudeModelPayload
    {
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
    }
}
