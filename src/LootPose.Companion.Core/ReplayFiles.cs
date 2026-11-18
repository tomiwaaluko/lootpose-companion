namespace LootPose.Companion;

/// <summary>A .replay file in Fortnite's Demos folder.</summary>
public sealed record ReplayFile(string Path, string Name, long Length, DateTime CreatedUtc, DateTime LastWriteUtc)
{
    /// <summary>name|size|last write ticks, as LED-N's Tracker keys a file. A file being written gets a new key each time.</summary>
    public string Key => $"{Name}|{Length}|{LastWriteUtc.Ticks}";
}

/// <summary>The Demos folder, as the engine sees it. The tests use a fake.</summary>
public interface IReplayFiles
{
    /// <summary>Every .replay file, oldest write first.</summary>
    IReadOnlyList<ReplayFile> List();

    /// <summary>True while the replay's decryption key is not written yet: the match is still being played.</summary>
    bool KeyMissing(string path);

    /// <summary>Reads one replay. Throws as ReplayReading.Read does.</summary>
    ReplayFacts Read(string path);
}

/// <summary>%LOCALAPPDATA%\FortniteGame\Saved\Demos. Every file in it is opened read-only.</summary>
public sealed class DemosFolder(string folder) : IReplayFiles
{
    public static string Default =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");

    public string Folder => folder;

    public IReadOnlyList<ReplayFile> List()
    {
        var directory = new DirectoryInfo(folder);
        if (!directory.Exists) return Array.Empty<ReplayFile>();
        try
        {
            return directory.EnumerateFiles("*.replay", SearchOption.TopDirectoryOnly)
                .Select(f => new ReplayFile(f.FullName, f.Name, f.Length, f.CreationTimeUtc, f.LastWriteTimeUtc))
                .OrderBy(f => f.LastWriteUtc)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file vanished or was locked while listing: the next look sees it.
            return Array.Empty<ReplayFile>();
        }
    }

    public bool KeyMissing(string path)
    {
        // The copied probe swallows access failures. Keep those retryable here, without changing vendor code.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new BinaryReader(stream);
        try
        {
            if (reader.ReadUInt32() != 0x1CA2E27Fu) return true;
            var version = reader.ReadUInt32();
            if (version >= 7)
            {
                var count = reader.ReadInt32();
                if (count < 0 || count > 64) return true;
                stream.Seek(count * 20L, SeekOrigin.Current);
            }
            stream.Seek(12, SeekOrigin.Current);
            var length = reader.ReadInt32();
            stream.Seek(length < 0 ? -2L * length : length, SeekOrigin.Current);
            reader.ReadUInt32(); // live
            reader.ReadInt64();  // timestamp
            reader.ReadUInt32(); // compressed
            if (reader.ReadUInt32() == 0) return false;
            var keyLength = reader.ReadInt32();
            if (keyLength <= 0 || keyLength > 256) return true;
            var key = reader.ReadBytes(keyLength);
            return key.Length != keyLength || key.All(b => b == 0);
        }
        catch (EndOfStreamException)
        {
            return true; // an incomplete header, rather than a temporary access failure
        }
    }

    public ReplayFacts Read(string path) => ReplayReading.Read(path);
}
