namespace LootPose.Companion.Tests;

public class CompanionInfoTests
{
    [Fact]
    public void Data_lives_in_the_Windows_users_own_app_data_folder()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.Equal(Path.Combine(appData, "LootPose Companion"), CompanionInfo.DataFolder);
    }

    [Fact]
    public void The_version_text_has_three_parts()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", CompanionInfo.VersionText);
    }

    [Fact]
    public void Addresses_point_at_LootPose_and_this_repository()
    {
        Assert.Equal("https://lootpose.vercel.app/", CompanionInfo.WebAppUrl);
        Assert.Equal("https://lootpose.vercel.app/data/treasures.json", CompanionInfo.DataFileUrl);
        Assert.Equal("https://api.github.com/repos/tomiwaaluko/lootpose-companion/releases/latest", CompanionInfo.LatestReleaseApi);
        Assert.Equal("https://github.com/tomiwaaluko/lootpose-companion/releases/latest", CompanionInfo.ReleasesPage);
    }
}
