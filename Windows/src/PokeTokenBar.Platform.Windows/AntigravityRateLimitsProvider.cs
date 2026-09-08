using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PokeTokenBar.Core;

namespace PokeTokenBar.Platform.Windows;

/// <summary>
/// Reads Antigravity's local Google OAuth token and queries Cloud Code's quota
/// summary endpoint. Prompt text, local conversations, and usage logs never leave
/// the machine; only the OAuth bearer token is sent to Google's quota endpoint.
/// </summary>
public sealed class AntigravityRateLimitsProvider : IRateLimitProvider
{
    private const string PrimaryEndpoint =
        "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuotaSummary";
    private const string DailyEndpoint =
        "https://daily-cloudcode-pa.googleapis.com/v1internal:retrieveUserQuotaSummary";
    private const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string GoogleClientId =
        "1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com";

    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<string> _tokenPaths;

    public AntigravityRateLimitsProvider(
        HttpClient httpClient,
        IEnumerable<string>? tokenPaths = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenPaths = (tokenPaths ?? ResolveTokenPaths())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string Id => "antigravity";

    public string DisplayName => "Antigravity";

    public async Task<ProviderRateLimitSnapshot?> FetchAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var credential = await ReadCredentialAsync(cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return null;
        }

        if (credential.IsExpired && !string.IsNullOrWhiteSpace(credential.RefreshToken))
        {
            credential = await TryRefreshAsync(credential, cancellationToken).ConfigureAwait(false)
                ?? credential;
        }

        var endpoints = ResolveEndpoints();
        Exception? lastError = null;
        foreach (var endpoint in endpoints)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
                request.Headers.UserAgent.ParseAdd("antigravity/2.9.1");
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return AntigravityRateLimitParser.Parse(json, now);
                }

                var status = (int)response.StatusCode;
                var detail = response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "요청이 너무 많습니다"
                    : response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                        ? "Google 로그인 토큰이 만료되었거나 거부되었습니다"
                        : $"HTTP {status}";
                lastError = new InvalidOperationException(
                    $"Antigravity 공식 한도 요청 실패: {detail}");
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    or HttpStatusCode.TooManyRequests)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                lastError = error;
            }
        }

        throw lastError ?? new InvalidOperationException("Antigravity 공식 한도 응답을 받지 못했습니다.");
    }

    /// <summary>
    /// Returns credential metadata without returning the OAuth token itself.
    /// </summary>
    public static AntigravityCredentialMetadata ParseCredentialMetadata(string content)
    {
        var credential = ParseCredential(content);
        return credential is null
            ? new AntigravityCredentialMetadata(false, false, null)
            : new AntigravityCredentialMetadata(true, credential.IsExpired, credential.ExpiresAt);
    }

    internal static IEnumerable<string> ResolveTokenPaths(
        string? userProfile = null,
        Func<string, string?>? environment = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        environment ??= Environment.GetEnvironmentVariable;

        var paths = new List<string>();
        foreach (var variable in new[] { "PTB_ANTIGRAVITY_TOKEN_FILE", "ANTIGRAVITY_TOKEN_FILE" })
        {
            var configured = environment(variable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                paths.Add(configured);
            }
        }

        paths.Add(Path.Combine(userProfile, ".gemini", "jetski-standalone-oauth-token"));
        paths.Add(Path.Combine(userProfile, ".gemini", "antigravity", "jetski-standalone-oauth-token"));
        return paths;
    }

    private static IEnumerable<string> ResolveEndpoints()
    {
        var endpoints = new List<string>();
        var cloudCodeUrl = Environment.GetEnvironmentVariable("CLOUD_CODE_URL");
        if (!string.IsNullOrWhiteSpace(cloudCodeUrl)
            && Uri.TryCreate(
                cloudCodeUrl.TrimEnd('/') + "/v1internal:retrieveUserQuotaSummary",
                UriKind.Absolute,
                out var configured))
        {
            endpoints.Add(configured.ToString());
        }

        endpoints.Add(DailyEndpoint);
        endpoints.Add(PrimaryEndpoint);
        return endpoints.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<AntigravityCredential?> ReadCredentialAsync(CancellationToken cancellationToken)
    {
        foreach (var path in _tokenPaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            string content;
            try
            {
                content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var credential = ParseCredential(content);
            if (credential is not null)
            {
                return credential;
            }
        }

        return null;
    }

    private async Task<AntigravityCredential?> TryRefreshAsync(
        AntigravityCredential credential,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credential.RefreshToken))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, GoogleTokenEndpoint);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = GoogleClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = credential.RefreshToken,
        });
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("access_token", out var accessToken)
                || accessToken.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(accessToken.GetString()))
            {
                return null;
            }

            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(
                root.TryGetProperty("expires_in", out var expiresIn)
                && expiresIn.TryGetDouble(out var seconds)
                    ? seconds
                    : 3600);
            return new AntigravityCredential(
                accessToken.GetString()!,
                credential.RefreshToken,
                expiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AntigravityCredential? ParseCredential(string content)
    {
        var raw = content.Trim();
        if (raw.StartsWith("go-keyring-base64:", StringComparison.OrdinalIgnoreCase))
        {
            var encoded = raw["go-keyring-base64:".Length..];
            try
            {
                raw = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var tokenValue = root.TryGetProperty("token", out var nested)
                ? nested
                : root;

            if (tokenValue.ValueKind == JsonValueKind.String)
            {
                var direct = tokenValue.GetString();
                return string.IsNullOrWhiteSpace(direct)
                    ? null
                    : new AntigravityCredential(direct!, null, null);
            }

            if (tokenValue.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var accessToken = GetString(tokenValue, "access_token")
                ?? GetString(tokenValue, "accessToken");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            var refreshToken = GetString(tokenValue, "refresh_token")
                ?? GetString(tokenValue, "refreshToken");
            var expiresAt = ParseExpiry(tokenValue);
            return new AntigravityCredential(accessToken!, refreshToken, expiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ParseExpiry(JsonElement token)
    {
        foreach (var property in new[] { "expiry", "expiresAt", "expires_at" })
        {
            if (!token.TryGetProperty(property, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(
                    value.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var unix))
            {
                var seconds = unix > 10_000_000_000 ? unix / 1000 : unix;
                try
                {
                    return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }
        }

        return null;
    }

    private static string? GetString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record AntigravityCredential(
        string AccessToken,
        string? RefreshToken,
        DateTimeOffset? ExpiresAt)
    {
        public bool IsExpired => ExpiresAt is { } expiry
            && expiry <= DateTimeOffset.UtcNow.AddMinutes(1);
    }
}

public sealed record AntigravityCredentialMetadata(
    bool HasAccessToken,
    bool IsExpired,
    DateTimeOffset? ExpiresAt);

public static class AntigravityRateLimitParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ProviderRateLimitSnapshot? Parse(string json, DateTimeOffset fetchedAt)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement, fetchedAt);
    }

    public static ProviderRateLimitSnapshot? Parse(JsonElement response, DateTimeOffset fetchedAt)
    {
        var payload = response.Deserialize<AntigravityQuotaPayload>(JsonOptions)
            ?? throw new JsonException("Antigravity 공식 한도 응답을 해석할 수 없습니다.");
        var windows = new List<RateLimitWindow>();
        foreach (var group in payload.Groups ?? [])
        {
            var groupName = GroupDisplayName(group.DisplayName);
            foreach (var bucket in group.Buckets ?? [])
            {
                if (bucket.RemainingFraction is null)
                {
                    continue;
                }

                var windowName = WindowDisplayName(bucket.Window, bucket.BucketId);
                windows.Add(new RateLimitWindow(
                    $"antigravity:{group.DisplayName ?? "group"}:{bucket.BucketId ?? windows.Count.ToString(CultureInfo.InvariantCulture)}",
                    $"{groupName} {windowName}",
                    (1 - bucket.RemainingFraction.Value) * 100,
                    ParseReset(bucket.ResetTime),
                    WindowDuration(bucket.Window, bucket.BucketId)));
            }
        }

        return windows.Count == 0
            ? null
            : new ProviderRateLimitSnapshot(
                "antigravity",
                "Antigravity",
                null,
                windows,
                fetchedAt);
    }

    private static string GroupDisplayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Antigravity";
        }

        if (raw.Contains("gemini", StringComparison.OrdinalIgnoreCase))
        {
            return "Gemini";
        }

        if (raw.Contains("claude", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("gpt", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("3p", StringComparison.OrdinalIgnoreCase))
        {
            return "외부 모델";
        }

        return raw;
    }

    private static string WindowDisplayName(string? window, string? bucketId)
    {
        if (IsFiveHourWindow(window, bucketId))
        {
            return "5시간 세션";
        }

        if (IsWeeklyWindow(window, bucketId))
        {
            return "주간";
        }

        return "한도";
    }

    private static int? WindowDuration(string? window, string? bucketId) =>
        IsFiveHourWindow(window, bucketId)
            ? 300
            : IsWeeklyWindow(window, bucketId)
                ? 10_080
                : null;

    private static bool IsFiveHourWindow(string? window, string? bucketId) =>
        string.Equals(window, "5h", StringComparison.OrdinalIgnoreCase)
        || bucketId?.Contains("5h", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsWeeklyWindow(string? window, string? bucketId) =>
        string.Equals(window, "weekly", StringComparison.OrdinalIgnoreCase)
        || bucketId?.Contains("weekly", StringComparison.OrdinalIgnoreCase) == true;

    private static DateTimeOffset? ParseReset(string? raw) =>
        DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;

    private sealed class AntigravityQuotaPayload
    {
        [JsonPropertyName("groups")]
        public List<AntigravityQuotaGroupPayload>? Groups { get; set; }
    }

    private sealed class AntigravityQuotaGroupPayload
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("buckets")]
        public List<AntigravityQuotaBucketPayload>? Buckets { get; set; }
    }

    private sealed class AntigravityQuotaBucketPayload
    {
        [JsonPropertyName("bucketId")]
        public string? BucketId { get; set; }

        [JsonPropertyName("window")]
        public string? Window { get; set; }

        [JsonPropertyName("resetTime")]
        public string? ResetTime { get; set; }

        [JsonPropertyName("remainingFraction")]
        public double? RemainingFraction { get; set; }
    }
}
