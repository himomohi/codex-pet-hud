using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;

namespace CodexPetLimitRings.Windows.Services;

public sealed class UsageService : IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(7) };
    private readonly string _authPath = Path.Combine(CodexPaths.Home, "auth.json");

    public async Task<UsageSnapshot?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var credentials = ReadCredentials();
        var accessToken = credentials?.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            AppLog.Write("Live usage unavailable: no Codex access token.");
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var assemblyVersion = typeof(UsageService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CodexPetLimitRings", assemblyVersion));
        request.Headers.Referrer = new Uri("https://chatgpt.com/");
        request.Headers.TryAddWithoutValidation("Origin", "https://chatgpt.com");
        if (!string.IsNullOrWhiteSpace(credentials?.AccountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credentials.AccountId);
        }
        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            AppLog.Write($"Live usage returned HTTP {(int)response.StatusCode}.");
            return null;
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Parse(document.RootElement);
    }

    private Credentials? ReadCredentials()
    {
        try
        {
            using var stream = new FileStream(_authPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("tokens", out var tokens) || tokens.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }
            var accessToken = tokens.TryGetProperty("access_token", out var token) && token.ValueKind is JsonValueKind.String
                ? token.GetString()
                : null;
            var accountId = tokens.TryGetProperty("account_id", out var account) && account.ValueKind is JsonValueKind.String
                ? account.GetString()
                : null;
            return new Credentials(accessToken, accountId);
        }
        catch { return null; }
    }

    internal static UsageSnapshot? Parse(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object) return null;
        var container = root.TryGetProperty("rate_limit", out var rateLimit) && rateLimit.ValueKind is JsonValueKind.Object
            ? rateLimit
            : root.TryGetProperty("rate_limits", out var rateLimits) && rateLimits.ValueKind is JsonValueKind.Object
                ? rateLimits
                : root;
        var primary = FindWindow(container, "primary", "primary_window");
        var secondary = FindWindow(container, "secondary", "secondary_window");
        WindowValue? weekly = primary?.WindowSeconds >= TimeSpan.FromDays(1).TotalSeconds
            ? primary
            : secondary?.WindowSeconds >= TimeSpan.FromDays(1).TotalSeconds
                ? secondary
                : null;

        if (root.TryGetProperty("additional_rate_limits", out var additional) &&
            additional.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in additional.EnumerateArray())
            {
                if (item.ValueKind is not JsonValueKind.Object) continue;
                var extraContainer = item.TryGetProperty("rate_limit", out var extraRateLimit) &&
                                     extraRateLimit.ValueKind is JsonValueKind.Object
                    ? extraRateLimit
                    : item.TryGetProperty("rateLimit", out var camelExtraRateLimit) &&
                      camelExtraRateLimit.ValueKind is JsonValueKind.Object
                        ? camelExtraRateLimit
                        : default;
                if (extraContainer.ValueKind is not JsonValueKind.Object) continue;

                var extraPrimary = FindWindow(extraContainer, "primary", "primary_window");
                if (extraPrimary?.Used is null ||
                    extraPrimary.WindowSeconds is not { } extraSeconds ||
                    extraSeconds >= TimeSpan.FromDays(1).TotalSeconds)
                {
                    continue;
                }

                var extraSecondary = FindWindow(extraContainer, "secondary", "secondary_window");
                primary = extraPrimary;
                secondary = weekly ?? extraSecondary;
                break;
            }
        }
        var primaryLooksWeekly = primary?.WindowSeconds >= TimeSpan.FromDays(1).TotalSeconds;
        var secondaryLooksShort = secondary?.WindowSeconds < TimeSpan.FromDays(1).TotalSeconds;
        if (primaryLooksWeekly && (secondary is null || secondaryLooksShort))
        {
            (primary, secondary) = (secondary, primary);
        }
        if (primary?.Used is null && secondary?.Used is null) return null;
        return new UsageSnapshot(
            primary?.Used,
            secondary?.Used,
            primary?.Reset,
            secondary?.Reset,
            "live",
            DateTimeOffset.Now);
    }

    private static WindowValue? FindWindow(JsonElement container, string preferred, string fallback)
    {
        if (container.ValueKind is not JsonValueKind.Object) return null;
        if (TryObject(container, preferred, out var preferredValue))
        {
            var parsed = ParseWindow(preferredValue);
            if (parsed.Used is not null) return parsed;
        }
        return TryObject(container, fallback, out var fallbackValue) ? ParseWindow(fallbackValue) : null;
    }

    private static WindowValue ParseWindow(JsonElement value)
    {
        double? used = TryDouble(value, "used_percent");
        if (used is null && TryDouble(value, "remaining_percent") is { } remaining)
        {
            used = 100 - remaining;
        }
        if (used is { } usedValue && !double.IsFinite(usedValue)) used = null;
        var reset = ReadReset(value);
        double? windowSeconds = TryDouble(value, "limit_window_seconds")
            ?? TryDouble(value, "window_seconds")
            ?? (TryDouble(value, "window_minutes") is { } minutes ? minutes * 60 : null);
        if (windowSeconds is { } duration && !double.IsFinite(duration)) windowSeconds = null;
        return new WindowValue(used, reset, windowSeconds);
    }

    private static bool TryObject(JsonElement parent, string name, out JsonElement value)
    {
        return parent.TryGetProperty(name, out value) && value.ValueKind is JsonValueKind.Object;
    }

    private static double? TryDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind is JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return value.ValueKind is JsonValueKind.String && double.TryParse(
            value.GetString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out number)
            ? number
            : null;
    }

    private static long? ReadReset(JsonElement value)
    {
        foreach (var name in new[] { "reset_at", "resets_at", "reset_time", "expires_at", "window_reset_at" })
        {
            if (!value.TryGetProperty(name, out var reset)) continue;
            if (reset.ValueKind is JsonValueKind.Number && reset.TryGetInt64(out var timestamp)) return ValidTimestamp(timestamp);
            if (reset.ValueKind is JsonValueKind.Number && reset.TryGetDouble(out var numericTimestamp) &&
                double.IsFinite(numericTimestamp) && numericTimestamp is >= long.MinValue and <= long.MaxValue)
            {
                return ValidTimestamp((long)numericTimestamp);
            }
            if (reset.ValueKind is JsonValueKind.String && long.TryParse(reset.GetString(), out timestamp))
            {
                return ValidTimestamp(timestamp);
            }
            if (reset.ValueKind is JsonValueKind.String && DateTimeOffset.TryParse(reset.GetString(), out var date))
            {
                return date.ToUnixTimeSeconds();
            }
        }
        foreach (var name in new[] { "reset_after_seconds", "seconds_until_reset", "reset_in_seconds" })
        {
            if (TryDouble(value, name) is { } seconds)
            {
                if (!double.IsFinite(seconds)) return null;
                try { return DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds(); }
                catch (ArgumentOutOfRangeException) { return null; }
            }
        }
        return null;
    }

    private static long? ValidTimestamp(long timestamp)
    {
        if (timestamp > 999_999_999_999) timestamp /= 1000;
        try
        {
            _ = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            return timestamp;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public void Dispose() => _client.Dispose();
    private sealed record Credentials(string? AccessToken, string? AccountId);
    private sealed record WindowValue(double? Used, long? Reset, double? WindowSeconds);
}
