namespace LootPose.Companion.Tests;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("v1.2.0", "1.1.9", true)]
    [InlineData("v1.2.0", "1.2.0", false)]
    [InlineData("1.3", "1.2.5", true)]
    [InlineData("v1.0.0", "1.2.0", false)]
    [InlineData("nightly", "1.2.0", false)]
    [InlineData(null, "1.2.0", false)]
    public void Compares_the_latest_release_tag_with_this_version(string? tag, string current, bool newer)
    {
        Assert.Equal(newer, UpdateCheck.IsNewer(tag, Version.Parse(current)));
    }

    [Fact]
    public async Task Asks_GitHub_for_the_latest_release_and_downloads_nothing_else()
    {
        var http = new FakeHttp().Reply("""{"tag_name":"v9.0.0","html_url":"https://github.com/tomiwaaluko/lootpose-companion/releases/tag/v9.0.0"}""");

        Assert.True(await new UpdateCheck(new HttpClient(http)).NewerExistsAsync(new Version(1, 0, 0), CancellationToken.None));

        var sent = http.Requests.Single();
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("https://api.github.com/repos/tomiwaaluko/lootpose-companion/releases/latest", sent.Uri!.ToString());
        Assert.StartsWith("LootPoseCompanion/", sent.Headers["User-Agent"]);
    }

    [Fact]
    public async Task No_connection_means_no_update_link()
    {
        var check = new UpdateCheck(new HttpClient(new FakeHttp().Fail()));
        Assert.False(await check.NewerExistsAsync(new Version(1, 0, 0), CancellationToken.None));
    }
}
