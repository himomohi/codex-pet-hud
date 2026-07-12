using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;

namespace CodexPetLimitRings.Windows.Services;

public sealed class UsageService : IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(7) };
    private readonly string _authPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");

    public async Task<UsageSnapshot?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var token = ReadToken();
        if (string.IsNullOrWhiteSpace(token)) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Parse(document.RootElement);
    }

    private string? ReadToken()
    {
        try
        {
            using var stream = new FileStream(_authPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("tokens", out var tokens) &&
                   tokens.TryGetProperty("access_token", out var value)
                ? value.GetString()
                : null;
        }
        catch { return null; }
    }

    private static UsageSnapshot? Parse(JsonElement root)
    {
        var container = root.TryGetProperty("rate_limit", out var rateLimit) ? rateLimit : root;
        var primary = FindWindow(container, "primary_window", "primary");
        var secondary = FindWindow(container, "secondary_window", "secondary");
        if (primary is null && secondary is null) return null;
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
        JsonElement value;
        if (!container.TryGetProperty(preferred, out value) && !container.TryGetProperty(fallback, out value)) return null;
        double? used = value.TryGetProperty("used_percent", out var usedElement) && usedElement.TryGetDouble(out var number) ? number : null;
        long? reset = value.TryGetProperty("reset_at", out var resetElement) && resetElement.TryGetInt64(out var timestamp) ? timestamp : null;
        return new WindowValue(used, reset);
    }

    public void Dispose() => _client.Dispose();
    private sealed record WindowValue(double? Used, long? Reset);
}
