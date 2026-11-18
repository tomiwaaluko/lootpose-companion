using ChestCounter;

namespace LootPose.Companion.Tests;

/// <summary>Proves the vendored files build and keep LED-N's rules.</summary>
public class VendorTests
{
    [Theory]
    [InlineData("Playlist_DefaultSquad", "/Delilah/Maps/Delilah_Terrain", false, true)]
    [InlineData("Playlist_NoBuildBR_Duo", "/Delilah/Maps/Delilah_Terrain", false, true)]
    [InlineData("Playlist_HabaneroSolo", "/Delilah/Maps/Delilah_Terrain", false, false)]
    [InlineData("Playlist_DefaultSquad", "/Delilah/Maps/Delilah_Terrain", true, false)]
    [InlineData("Playlist_DefaultSquad", "/Figment/Maps/Figment_Terrain", false, false)]
    public void LED_Ns_rule_for_which_matches_count_is_unchanged(string playlist, string map, bool custom, bool counts)
    {
        var match = new MatchRecord { Playlist = playlist, Map = map, IsCustomMatch = custom };
        Assert.Equal(counts, GameModes.Counts(match));
    }

    [Theory]
    [InlineData("Tiered_Chest_Athena_C", true)]
    [InlineData("Tiered_Ammo_Athena_C", false)]
    [InlineData("B_LootCannon_C", false)]
    [InlineData("FoodBox_Produce_Athena_C", false)]
    public void LED_Ns_chest_kinds_are_unchanged(string blueprint, bool isChest)
    {
        Assert.Equal(isChest, ChestKinds.IsChest(ChestKinds.Classify(blueprint)));
    }

    [Fact]
    public void The_header_probe_reads_the_live_flag_and_whether_the_key_is_written()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lootpose-{Guid.NewGuid():N}.replay");
        try
        {
            File.WriteAllBytes(path, ReplayBytes.Header(live: true, encrypted: true, keyWritten: false));
            Assert.True(ReplayHeaderProbe.IsLive(path));
            Assert.True(ReplayHeaderProbe.KeyMissing(path));

            File.WriteAllBytes(path, ReplayBytes.Header(live: false, encrypted: true, keyWritten: true));
            Assert.False(ReplayHeaderProbe.IsLive(path));
            Assert.False(ReplayHeaderProbe.KeyMissing(path));

            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            Assert.Null(ReplayHeaderProbe.IsLive(path));
            Assert.True(ReplayHeaderProbe.KeyMissing(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
