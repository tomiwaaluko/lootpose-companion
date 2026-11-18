namespace LootPose.Companion;

public enum SyncState
{
    Synced,
    Offline,
    NotLinked,
    RateLimited,
    EventEnded,
    Rejected,
}

/// <summary>The result of one sync. <see cref="Wait"/> is how long the server asked us to wait, when it did.</summary>
public readonly record struct SyncResult(SyncState State, TimeSpan? Wait = null);

public interface ISyncClient
{
    /// <summary>Sends the waiting push or checks idle link/reset state. Network failures are results.</summary>
    Task<SyncResult> SyncAsync(CancellationToken cancel);
}

/// <summary>The spec's "How the companion syncs", for the push waiting in the ledger.</summary>
public sealed class SyncClient(DeviceApi api, Ledger ledger) : ISyncClient
{
    public async Task<SyncResult> SyncAsync(CancellationToken cancel)
    {
        if (!ledger.IsLinked) return new SyncResult(SyncState.NotLinked);
        if (ledger.Data.Pending is null)
        {
            var refreshed = await ResolveConflictAsync(cancel).ConfigureAwait(false);
            if (refreshed is { } final) return final;
        }

        // A conflict costs one status call and one more push, so three rounds are always enough.
        for (var round = 0; round < 3; round++)
        {
            if (ledger.Data.Pending is not { } pending) return new SyncResult(SyncState.Synced);

            DeviceReply reply;
            try
            {
                reply = await api.PushAsync(ledger.Data.DeviceId!, ledger.SecretHex(), pending.RequestId,
                    ledger.Data.Revision, ledger.Data.Epoch, pending.Buckets, cancel).ConfigureAwait(false);
            }
            catch (DeviceApiException)
            {
                return new SyncResult(SyncState.Offline); // the same request id goes again next time
            }

            if (reply.Ok)
            {
                ledger.Pushed(pending, reply.Revision, reply.Epoch);
                continue;
            }

            switch (reply.Error)
            {
                case "conflict":
                    var resolved = await ResolveConflictAsync(cancel).ConfigureAwait(false);
                    if (resolved is { } final) return final;
                    break;
                case "not_linked":
                    ledger.Forget();
                    return new SyncResult(SyncState.NotLinked);
                case "rate_limited":
                    return new SyncResult(SyncState.RateLimited, WaitOf(reply));
                case "event_ended":
                    ledger.DropPending(SyncState.EventEnded);
                    return new SyncResult(SyncState.EventEnded);
                case "invalid_counts":
                case "invalid_request":
                    ledger.DropPending(SyncState.Rejected);
                    return new SyncResult(SyncState.Rejected);
                default:
                    return new SyncResult(SyncState.Offline);
            }
        }
        return new SyncResult(ledger.Data.Pending is null ? SyncState.Synced : SyncState.Offline);
    }

    /// <summary>
    /// Asks for the row. Same epoch: adopt its revision and push again. Higher epoch: the account was reset, so every
    /// counted match becomes "Before a reset" and the row starts from zero. Returns a final result, or null to push again.
    /// </summary>
    private async Task<SyncResult?> ResolveConflictAsync(CancellationToken cancel)
    {
        DeviceReply status;
        try
        {
            status = await api.StatusAsync(ledger.Data.DeviceId!, ledger.SecretHex(), cancel).ConfigureAwait(false);
        }
        catch (DeviceApiException)
        {
            return new SyncResult(SyncState.Offline);
        }

        if (!status.Ok)
        {
            if (status.Error == "not_linked")
            {
                ledger.Forget();
                return new SyncResult(SyncState.NotLinked);
            }
            return status.Error == "rate_limited"
                ? new SyncResult(SyncState.RateLimited, WaitOf(status))
                : new SyncResult(SyncState.Offline);
        }

        if (status.Epoch > ledger.Data.Epoch)
        {
            ledger.Reset(status.Epoch, status.Revision);
            ledger.QueuePushIfDifferent(status.Buckets);
        }
        else
        {
            ledger.AdoptRevision(status.Revision);
        }
        return null;
    }

    private static TimeSpan? WaitOf(DeviceReply reply) => reply.RetryAfter is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
}
