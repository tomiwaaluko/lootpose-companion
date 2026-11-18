using System.Text.Json;

namespace LootPose.Companion.Tests;

public class BucketingTests
{
    private sealed record Example(string Name, string DataFile, string Instant, string Bucket);

    private sealed record Fixtures(Dictionary<string, GameData> DataFiles, List<Example> Examples);

    // A copy of chest-tracker's docs/fixtures/bucketing.json. The web app's TypeScript test runs the same examples.
    private static readonly Lazy<Fixtures> Shared = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "bucketing.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var files = new Dictionary<string, GameData>();
        foreach (var file in document.RootElement.GetProperty("dataFiles").EnumerateObject())
        {
            files[file.Name] = GameData.Parse(file.Value.GetRawText());
        }
        var examples = document.RootElement.GetProperty("examples").EnumerateArray()
            .Select(e => new Example(
                e.GetProperty("name").GetString()!,
                e.GetProperty("dataFile").GetString()!,
                e.GetProperty("instant").GetString()!,
                e.GetProperty("bucket").GetString()!))
            .ToList();
        return new Fixtures(files, examples);
    });

    public static TheoryData<string> ExampleNames()
    {
        var names = new TheoryData<string>();
        foreach (var example in Shared.Value.Examples)
        {
            names.Add(example.Name);
        }
        return names;
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void Every_shared_example_gets_the_same_bucket_as_in_the_web_app(string name)
    {
        var example = Shared.Value.Examples.Single(e => e.Name == name);
        var data = Shared.Value.DataFiles[example.DataFile];
        Assert.Equal(example.Bucket, Bucketing.BucketFor(data, GameData.Instant(example.Instant)));
    }

    [Fact]
    public void The_copy_covers_the_cases_the_spec_names()
    {
        var names = Shared.Value.Examples.Select(e => e.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var needed in new[]
        {
            "a normal week", "the minute before a weekly reset", "the minute after a weekly reset",
            "the first second of a Treasure Hour", "the last second of a Treasure Hour", "an expected Treasure Hour",
        })
        {
            Assert.Contains(needed, names);
        }
    }

    [Fact]
    public void The_event_ends_at_its_first_instant()
    {
        var data = GameData.Parse("""{"weeklyReset":{"day":"sunday","timeUtc":"14:00"},"eventEnd":"2026-12-05T19:00:00Z","treasureHours":[]}""");
        Assert.False(Bucketing.EventEnded(data, GameData.Instant("2026-12-05T18:59:59.999Z")));
        Assert.True(Bucketing.EventEnded(data, GameData.Instant("2026-12-05T19:00:00Z")));
    }

    [Fact]
    public void A_broken_data_file_is_refused()
    {
        Assert.Throws<FormatException>(() => GameData.Parse("""{"weeklyReset":{"day":"someday","timeUtc":"14:00"},"treasureHours":[]}"""));
        Assert.Throws<FormatException>(() => GameData.Parse("""{"weeklyReset":{"day":"sunday","timeUtc":"2pm"},"treasureHours":[]}"""));
        Assert.Throws<FormatException>(() => GameData.Parse("""{"weeklyReset":{"day":"sunday","timeUtc":"14:00"}}"""));
        Assert.Throws<FormatException>(() => GameData.Parse("not json"));
    }

    [Fact]
    public void Instants_are_cut_to_the_millisecond_as_in_JavaScript()
    {
        var data = GameData.Parse("""{"weeklyReset":{"day":"sunday","timeUtc":"14:00"},"treasureHours":[{"id":"th","start":"2026-11-08T02:00:00Z","end":"2026-11-08T04:00:00Z"}]}""");
        var lastTickOfTheHour = GameData.Instant("2026-11-08T03:59:59.999Z").AddTicks(9999);
        Assert.Equal("2026-11-01|th", Bucketing.BucketFor(data, lastTickOfTheHour));
    }
}
