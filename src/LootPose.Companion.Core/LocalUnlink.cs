namespace LootPose.Companion;

public static class LocalUnlink
{
    public static Task<bool> TryAsync(Ledger ledger, ISyncClient? sync, CancellationToken cancel)
    {
        ledger.Forget();
        return Task.FromResult(true);
    }
}
