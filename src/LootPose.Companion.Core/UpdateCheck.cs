using System.Text.Json;

namespace LootPose.Companion;

/// <summary>At start and once a day: is there a newer release? It only shows a link and never downloads anything.</summary>
public sealed class UpdateCheck(HttpClient http, string address = CompanionInfo.LatestReleaseApi)
{
    public async Task<bool> NewerExistsAsync(Version current, CancellationToken cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            // GitHub's API needs a User-Agent. A generic one: nothing about the player.
            request.Headers.UserAgent.ParseAdd($"LootPoseCompanion/{Normalize(current).ToString(3)}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
            return document.RootElement.TryGetProperty("tag_name", out var tag)
                && tag.ValueKind == JsonValueKind.String
                && IsNewer(tag.GetString(), current);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            if (cancel.IsCancellationRequested) throw;
            return false;
        }
    }

    /// <summary>"v1.2.0" is newer than 1.1.9. A tag that is not a version is never newer.</summary>
    public static bool IsNewer(string? tag, Version current)
    {
        var text = tag?.TrimStart('v', 'V') ?? "";
        return Version.TryParse(text, out var latest) && Normalize(latest) > Normalize(current);
    }

    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));
}
