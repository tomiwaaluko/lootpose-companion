// LootPose Companion: the ReplayHeaderProbe class, copied unchanged from LED-N's src/ChestCounter/Tracker.cs
// (lines 670 to 779 at commit c4ca1400ff1f79dbee4ea3ecb3f8eb72c8e6bb82). See vendor/UPSTREAM.md.
namespace ChestCounter;

/// <summary>Reads just enough of the replay file header to know whether the match is still being recorded.</summary>
public static class ReplayHeaderProbe
{
    /// <summary>
    /// The map of the match (e.g. /Delilah/Maps/Delilah_Terrain), from the header chunk: written at the start of the
    /// recording, never encrypted. Null when it cannot be read (yet).
    /// </summary>
    public static string? Map(string path)
    {
        try
        {
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                // The header is the first chunk: a few kilobytes are enough.
                bytes = new byte[Math.Min(fs.Length, 256 * 1024)];
                fs.ReadExactly(bytes);
            }
            using var ms = new MemoryStream(bytes, writable: false);
            var reader = new FortniteReplayReader.ReplayReader(null, Unreal.Core.Models.Enums.ParseMode.EventsOnly);
            var replay = reader.ReadReplay(ms);
            return replay.Header?.LevelNamesAndTimes?.Select(l => l.Item1).FirstOrDefault(l => !string.IsNullOrEmpty(l));
        }
        catch
        {
            return null;
        }
    }

    private const uint FileMagic = 0x1CA2E27F;

    /// <summary>
    /// True when the replay is encrypted and its key is not written yet. Fortnite writes the real key only when
    /// the match ends (all zeros before): the data of a match in progress cannot be read.
    /// </summary>
    public static bool KeyMissing(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt32() != FileMagic) return true;
            var fileVersion = br.ReadUInt32();
            if (fileVersion >= 7)
            {
                var count = br.ReadInt32();
                if (count < 0 || count > 64) return true;
                fs.Seek(count * 20L, SeekOrigin.Current);
            }
            fs.Seek(12, SeekOrigin.Current);
            var len = br.ReadInt32();
            fs.Seek(len < 0 ? -2L * len : len, SeekOrigin.Current);
            br.ReadUInt32(); // bIsLive
            br.ReadInt64(); // timestamp
            br.ReadUInt32(); // compressed
            if (br.ReadUInt32() == 0) return false; // not encrypted
            var keyLen = br.ReadInt32();
            if (keyLen <= 0 || keyLen > 256) return true;
            return br.ReadBytes(keyLen).All(b => b == 0);
        }
        catch
        {
            return true;
        }
    }

    public static bool? IsLive(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt32() != FileMagic)
            {
                return null;
            }

            var fileVersion = br.ReadUInt32();
            if (fileVersion >= 7)
            {
                var count = br.ReadInt32();
                if (count < 0 || count > 64)
                {
                    return null;
                }
                fs.Seek(count * 20L, SeekOrigin.Current);
            }

            br.ReadUInt32(); // LengthInMS
            br.ReadUInt32(); // NetworkVersion
            br.ReadUInt32(); // Changelist

            var len = br.ReadInt32(); // FriendlyName (FString)
            if (len < 0)
            {
                fs.Seek(-2L * len, SeekOrigin.Current);
            }
            else
            {
                fs.Seek(len, SeekOrigin.Current);
            }

            return br.ReadUInt32() != 0;
        }
        catch
        {
            return null;
        }
    }
}
