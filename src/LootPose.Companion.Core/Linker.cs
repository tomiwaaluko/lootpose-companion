namespace LootPose.Companion;

public enum LinkResult
{
    Linked,
    TimedOut,
    Cancelled,
}

/// <summary>After Link this PC: asks device_request for the row every 3 seconds, for up to 10 minutes.</summary>
public sealed class Linker(DeviceApi api, Ledger ledger, Func<DateTimeOffset> clock, Func<TimeSpan, CancellationToken, Task> delay)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);

    public async Task<LinkResult> WaitAsync(CancellationToken cancel)
    {
        if (ledger.IsLinked) return LinkResult.Linked;
        var (deviceId, secret) = ledger.LinkCredentials();
        var secretHex = Convert.ToHexStringLower(secret);
        var started = clock();
        try
        {
            while (clock() - started < Limit)
            {
                var wait = Interval;
                try
                {
                    var reply = await api.StatusAsync(deviceId, secretHex, cancel).ConfigureAwait(false);
                    if (reply.Ok)
                    {
                        // A new link's row is empty at revision 0, under the account's current epoch.
                        ledger.CompleteLink(clock(), reply.Revision, reply.Epoch);
                        return LinkResult.Linked;
                    }
                    // not_linked is the answer until the player presses Link in the browser.
                    if (reply.Error == "rate_limited" && reply.RetryAfter is { } seconds) wait = TimeSpan.FromSeconds(seconds);
                }
                catch (DeviceApiException)
                {
                    // Offline for a moment: keep asking.
                }
                await delay(wait, cancel).ConfigureAwait(false);
            }
            return LinkResult.TimedOut;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return LinkResult.Cancelled;
        }
    }
}
