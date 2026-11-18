namespace LootPose.Companion.Tests;

/// <summary>The start of a replay file, as far as LED-N's ReplayHeaderProbe reads it. Not a whole replay.</summary>
internal static class ReplayBytes
{
    public static byte[] Header(bool live, bool encrypted, bool keyWritten)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x1CA2E27Fu);         // file magic
        writer.Write(6u);                  // file version: below 7, so no extra list follows
        writer.Write(0u);                  // length in ms
        writer.Write(0u);                  // network version
        writer.Write(0u);                  // changelist
        writer.Write(0);                   // friendly name: an empty string
        writer.Write(live ? 1u : 0u);      // still being recorded
        writer.Write(0L);                  // timestamp
        writer.Write(0u);                  // compressed
        writer.Write(encrypted ? 1u : 0u); // encrypted
        writer.Write(32);                  // key length
        writer.Write(Enumerable.Repeat(keyWritten ? (byte)7 : (byte)0, 32).ToArray());
        writer.Flush();
        return stream.ToArray();
    }
}
