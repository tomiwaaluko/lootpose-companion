namespace LootPose.Companion;

/// <summary>Names and addresses shared by the whole app.</summary>
public static class CompanionInfo
{
    public const string ProductName = "LootPose Companion";
    public const string WebAppUrl = "https://lootpose.vercel.app/";
    public const string DataFileUrl = "https://lootpose.vercel.app/data/treasures.json";
    public const string Repository = "tomiwaaluko/lootpose-companion";
    public const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";

    /// <summary>The version this copy was built as. A release tag v1.2.3 builds 1.2.3.</summary>
    public static Version Version { get; } = typeof(CompanionInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>"1.2.3".</summary>
    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Math.Max(0, Version.Build)}";

    /// <summary>%APPDATA%\LootPose Companion. Each Windows user has their own.</summary>
    public static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductName);
}
