namespace LootPose.Companion;

/// <summary>Called inside the engine gate: confirm waiting counts before forgetting local credentials.</summary>
public static class LocalUnlink
{
    public static async Task<bool> TryAsync(Ledger ledger, ISyncClient? sync, CancellationToken cancel)
    {
        if (!ledger.IsLinked) return true;
        if (ledger.Data.Pending is not null)
        {
            if (sync is null) return false;
            var result = await sync.SyncAsync(cancel).ConfigureAwait(false);
            if (result.State != SyncState.Synced && ledger.IsLinked) return false;
            if (ledger.Data.Pending is not null) return false;
        }
        ledger.Forget();
        return true;
    }
}
