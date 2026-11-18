using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LootPose.Companion;

/// <summary>A reply from public.device_request. A failure has Ok false and an error code from plan 1's contract.</summary>
public sealed record DeviceReply(bool Ok, Dictionary<string, int> Buckets, int Revision, int Epoch, string? Error, int? RetryAfter)
{
    public static DeviceReply Failure(string error, int? retryAfter = null) => new(false, new Dictionary<string, int>(), 0, 0, error, retryAfter);
}

/// <summary>The request did not reach the function: no connection, a time-out, or an HTTP status other than 200.</summary>
public sealed class DeviceApiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Calls public.device_request as plan 1's "Request contracts" describes: POST {project}/rest/v1/rpc/device_request,
/// the publishable key in the apikey header, and the body {"input": {...}}. Every reply that reaches the function is
/// HTTP 200, success or failure.
/// </summary>
public sealed class DeviceApi(HttpClient http, ServerConfig server)
{
    /// <summary>
    /// Supabase's API key guide: publishable keys go in the apikey header, not in Authorization: Bearer.
    /// Task 7 Step 1 of the plan checks this against the live project. Set it to true only if that check needed Bearer.
    /// </summary>
    public static readonly bool SendKeyAsBearer = false;

    public Task<DeviceReply> StatusAsync(string deviceId, string secretHex, CancellationToken cancel) =>
        CallAsync(new JsonObject { ["action"] = "status", ["deviceId"] = deviceId, ["secret"] = secretHex }, cancel);

    /// <summary>Replaces the row with <paramref name="buckets"/>, the PC's whole row.</summary>
    public Task<DeviceReply> PushAsync(string deviceId, string secretHex, string requestId, int expectedRevision, int epoch,
        IReadOnlyDictionary<string, int> buckets, CancellationToken cancel)
    {
        var row = new JsonObject();
        foreach (var (key, count) in buckets.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            row[key] = count;
        }
        return CallAsync(new JsonObject
        {
            ["action"] = "push",
            ["deviceId"] = deviceId,
            ["secret"] = secretHex,
            ["requestId"] = requestId,
            ["expectedRevision"] = expectedRevision,
            ["epoch"] = epoch,
            ["buckets"] = row,
        }, cancel);
    }

    /// <summary>Reads a reply. Anything that is not the shape plan 1 promises is treated as "unavailable".</summary>
    public static DeviceReply Parse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out var ok)) return DeviceReply.Failure("unavailable");
            if (ok.ValueKind == JsonValueKind.True)
            {
                var buckets = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var bucket in root.GetProperty("buckets").EnumerateObject())
                {
                    buckets[bucket.Name] = bucket.Value.GetInt32();
                }
                return new DeviceReply(true, buckets, root.GetProperty("revision").GetInt32(), root.GetProperty("epoch").GetInt32(), null, null);
            }
            var error = root.TryGetProperty("error", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString()! : "unavailable";
            int? retryAfter = root.TryGetProperty("retryAfter", out var wait) && wait.ValueKind == JsonValueKind.Number ? wait.GetInt32() : null;
            return DeviceReply.Failure(error, retryAfter);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return DeviceReply.Failure("unavailable");
        }
    }

    private async Task<DeviceReply> CallAsync(JsonObject input, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server.Address, "rest/v1/rpc/device_request"))
        {
            Content = new StringContent(new JsonObject { ["input"] = input }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("apikey", server.PublishableKey);
        if (SendKeyAsBearer) request.Headers.Authorization = new("Bearer", server.PublishableKey);

        string body;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new DeviceApiException($"device_request answered HTTP {(int)response.StatusCode}.");
            }
            body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            throw new DeviceApiException("device_request could not be reached.", e);
        }
        return Parse(body);
    }
}
