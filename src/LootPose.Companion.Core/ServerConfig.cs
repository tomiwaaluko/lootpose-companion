using System.Reflection;

namespace LootPose.Companion;

/// <summary>
/// LootPose's Supabase project address and publishable key. Both are public by design. A release build has them
/// from the repository variables LOOTPOSE_SUPABASE_URL and LOOTPOSE_SUPABASE_KEY (the exe project turns them into
/// assembly metadata). A local run can set environment variables with the same names, which win.
/// </summary>
public sealed record ServerConfig(Uri Address, string PublishableKey)
{
    public static ServerConfig? Find(Assembly? entry)
    {
        var url = Environment.GetEnvironmentVariable("LOOTPOSE_SUPABASE_URL") ?? Metadata(entry, "SupabaseUrl");
        var key = Environment.GetEnvironmentVariable("LOOTPOSE_SUPABASE_KEY") ?? Metadata(entry, "SupabasePublishableKey");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return null;
        if (!Uri.TryCreate(url.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps) return null;
        return new ServerConfig(address, key.Trim());
    }

    private static string? Metadata(Assembly? assembly, string key) =>
        assembly?.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;
}
