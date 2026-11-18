using System.Security.Cryptography;

namespace LootPose.Companion;

/// <summary>Plan 1's "The link address". The secret itself is never in the address.</summary>
public static class LinkCodes
{
    /// <summary>The SHA-256 of the 32 secret bytes, as 64 lowercase hex characters.</summary>
    public static string Fingerprint(byte[] secret) => Convert.ToHexStringLower(SHA256.HashData(secret));

    /// <summary>The first six characters of the fingerprint in capitals, as XXX-XXX. The web app shows the same.</summary>
    public static string Code(string fingerprint) => $"{fingerprint[..3]}-{fingerprint[3..6]}".ToUpperInvariant();

    /// <summary>https://lootpose.vercel.app/?link=(device id)&amp;k=(fingerprint)</summary>
    public static Uri LinkAddress(string deviceId, string fingerprint) =>
        new($"{CompanionInfo.WebAppUrl}?link={deviceId}&k={fingerprint}");
}
