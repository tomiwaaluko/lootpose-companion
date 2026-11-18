using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LootPose.Companion;

/// <summary>One match the companion handled.</summary>
public sealed class MatchEntry
{
    /// <summary>The replay's session id, or "file:" and the file key for a replay that was not read.</summary>
    public string Key { get; set; } = "";

    public DateTimeOffset? StartUtc { get; set; }

    /// <summary>The playlist as LED-N reads it. Null for a replay that was not read.</summary>
    public string? Playlist { get; set; }

    public MatchOutcome Outcome { get; set; }

    public int Chests { get; set; }

    public Dictionary<string, int> Buckets { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The device id of the link this match was counted under.</summary>
    public string? DeviceId { get; set; }

    /// <summary>True for a replay whose recording was cut (its key never written), not one LED-N failed to read.</summary>
    public bool CutShort { get; set; }

    public DateTimeOffset HandledAt { get; set; }
}

/// <summary>A push saved before it is sent, so that a lost reply is sent again with the same request id.</summary>
public sealed class PendingPush
{
    public string RequestId { get; set; } = "";

    public Dictionary<string, int> Buckets { get; set; } = new(StringComparer.Ordinal);
}

public sealed class LedgerData
{
    public int Version { get; set; } = 1;

    public string? DeviceId { get; set; }

    /// <summary>The 32-byte secret, encrypted for this Windows user with DPAPI, as base64.</summary>
    public string? ProtectedSecret { get; set; }

    /// <summary>When the link was confirmed. Null while the PC is not linked.</summary>
    public DateTimeOffset? LinkedAt { get; set; }

    public int Revision { get; set; }

    public int Epoch { get; set; }

    public PendingPush? Pending { get; set; }

    public List<MatchEntry> Matches { get; set; } = new();

    /// <summary>
    /// Replay files already handled. Key: name|size|last write ticks. Value: the match's key, or "unreadable:" and the
    /// version of the app that could not read it.
    /// </summary>
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// %APPDATA%\LootPose Companion\ledger.json: every handled match, the link, the protected secret, the row's revision
/// and epoch, and the push waiting to be sent. Written atomically after every change. Not thread-safe: the engine
/// changes it from one place at a time.
/// </summary>
public sealed class Ledger
{
    public const string UnreadablePrefix = "unreadable:";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LootPose Companion");

    private readonly string _path;

    private Ledger(string path, LedgerData data)
    {
        _path = path;
        Data = data;
    }

    public LedgerData Data { get; }

    public bool IsLinked => Data.DeviceId is not null && Data.ProtectedSecret is not null && Data.LinkedAt is not null;

    public static Ledger Open(string folder)
    {
        var path = Path.Combine(folder, "ledger.json");
        LedgerData data;
        try
        {
            data = File.Exists(path) ? JsonSerializer.Deserialize<LedgerData>(File.ReadAllText(path), Json) ?? new LedgerData() : new LedgerData();
        }
        catch (JsonException)
        {
            // Kept for a bug report. Starting empty means not linked, so nothing can be counted twice.
            File.Copy(path, $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
            data = new LedgerData();
        }
        var ledger = new Ledger(path, data);
        ledger.CheckSecret();
        return ledger;
    }

    public void Save() => AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(Data, Json));

    /// <summary>This PC's row: every match counted under the current link.</summary>
    public Dictionary<string, int> Buckets()
    {
        var sum = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!IsLinked) return sum;
        foreach (var match in Data.Matches)
        {
            if (match.Outcome != MatchOutcome.Counted || match.DeviceId != Data.DeviceId) continue;
            foreach (var (key, count) in match.Buckets)
            {
                sum[key] = sum.GetValueOrDefault(key) + count;
            }
        }
        return sum;
    }

    /// <summary>The device id and secret to link with. Made once, and made new after a link is lost.</summary>
    public (string DeviceId, byte[] Secret) LinkCredentials()
    {
        if (Data.DeviceId is null || Data.ProtectedSecret is null)
        {
            Data.DeviceId = Guid.NewGuid().ToString();
            Data.ProtectedSecret = Protect(RandomNumberGenerator.GetBytes(32));
            Data.LinkedAt = null;
            Data.Revision = 0;
            Data.Epoch = 0;
            Data.Pending = null;
            Save();
        }
        return (Data.DeviceId!, Unprotect(Data.ProtectedSecret!));
    }

    /// <summary>The secret as 64 lowercase hex characters, as device_request takes it.</summary>
    public string SecretHex() =>
        Convert.ToHexStringLower(Unprotect(Data.ProtectedSecret ?? throw new InvalidOperationException("This PC has no secret.")));

    public void CompleteLink(DateTimeOffset at, int revision, int epoch)
    {
        Data.LinkedAt = at;
        Data.Revision = revision;
        Data.Epoch = epoch;
        Data.Pending = null;
        Save();
    }

    /// <summary>After not_linked, or Unlink this PC. The matches stay, so none of them is ever counted again.</summary>
    public void Forget()
    {
        Data.DeviceId = null;
        Data.ProtectedSecret = null;
        Data.LinkedAt = null;
        Data.Revision = 0;
        Data.Epoch = 0;
        Data.Pending = null;
        Save();
    }

    /// <summary>
    /// Records a match from a replay, or one listed without being read. Returns false, and counts nothing, when that
    /// match was handled before: the same match in a renamed file, or a match from an earlier link.
    /// </summary>
    public bool Record(MatchEntry entry, string fileKey)
    {
        Data.Matches.RemoveAll(m => m.Key == "file:" + fileKey && m.Outcome == MatchOutcome.CouldNotBeRead);
        Data.Files[fileKey] = entry.Key;
        var added = !Data.Matches.Any(m => m.Key == entry.Key);
        if (added)
        {
            if (entry.Outcome == MatchOutcome.Counted) entry.DeviceId = Data.DeviceId;
            Data.Matches.Add(entry);
            if (entry.Outcome == MatchOutcome.Counted) QueuePush();
        }
        Save();
        return added;
    }

    /// <summary>A replay that could not be read. It is tried again when the app's version changes.</summary>
    public void RecordUnreadable(string fileKey, DateTimeOffset? startUtc, string appVersion, DateTimeOffset now, bool cutShort)
    {
        var key = "file:" + fileKey;
        Data.Files[fileKey] = UnreadablePrefix + appVersion;
        Data.Matches.RemoveAll(m => m.Key == key);
        Data.Matches.Add(new MatchEntry { Key = key, StartUtc = startUtc, Outcome = MatchOutcome.CouldNotBeRead, CutShort = cutShort, HandledAt = now });
        Save();
    }

    /// <summary>A push was accepted. Its pending copy is cleared only if no newer push replaced it meanwhile.</summary>
    public void Pushed(PendingPush sent, int revision, int epoch)
    {
        Data.Revision = revision;
        Data.Epoch = epoch;
        if (Data.Pending?.RequestId == sent.RequestId) Data.Pending = null;
        Save();
    }

    public void AdoptRevision(int revision)
    {
        Data.Revision = revision;
        Save();
    }

    /// <summary>
    /// The account was reset: every match counted under this link becomes "Before a reset", the row is empty, and the
    /// new epoch is adopted. Matches played from now on count.
    /// </summary>
    public void Reset(int epoch, int revision)
    {
        foreach (var match in Data.Matches.Where(m => m.Outcome == MatchOutcome.Counted && m.DeviceId == Data.DeviceId))
        {
            match.Outcome = MatchOutcome.BeforeReset;
        }
        Data.Epoch = epoch;
        Data.Revision = revision;
        Data.Pending = null;
        Save();
    }

    /// <summary>Saves a push when the server's row differs from this PC's.</summary>
    public void QueuePushIfDifferent(IReadOnlyDictionary<string, int> server)
    {
        if (SameBuckets(Buckets(), server)) return;
        QueuePush();
        Save();
    }

    public void DropPending()
    {
        Data.Pending = null;
        Save();
    }

    /// <summary>A missing bucket and a bucket of 0 are the same.</summary>
    public static bool SameBuckets(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b) =>
        a.Keys.Union(b.Keys).All(key => a.GetValueOrDefault(key) == b.GetValueOrDefault(key));

    /// <summary>The whole row, under a new request id. The caller saves it in the same write as its cause.</summary>
    private void QueuePush() => Data.Pending = new PendingPush { RequestId = Guid.NewGuid().ToString(), Buckets = Buckets() };

    /// <summary>A ledger copied from another Windows user cannot decrypt its secret: that PC is not linked.</summary>
    private void CheckSecret()
    {
        if (Data.ProtectedSecret is null) return;
        try
        {
            Unprotect(Data.ProtectedSecret);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            Forget();
        }
    }

    private static string Protect(byte[] secret) =>
        Convert.ToBase64String(ProtectedData.Protect(secret, Entropy, DataProtectionScope.CurrentUser));

    private static byte[] Unprotect(string protectedSecret) =>
        ProtectedData.Unprotect(Convert.FromBase64String(protectedSecret), Entropy, DataProtectionScope.CurrentUser);
}
