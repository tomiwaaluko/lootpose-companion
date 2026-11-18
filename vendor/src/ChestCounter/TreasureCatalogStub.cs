// LootPose Companion: stands in for LED-N's src/ChestCounter/TreasureCatalog.cs, which loads content.json (treasure
// weeks, prize names, odds, notices) and needs LED-N's AppInfo and an embedded file. The companion only counts chests,
// so no prize is ever named: TreasureNames.Friendly then falls back to the raw prize id, as LED-N does for a prize it
// does not know. See vendor/UPSTREAM.md.
namespace ChestCounter;

/// <summary>A treasure prize, as far as TreasureNames reads one.</summary>
public sealed record TreasurePrize(string Id, string Kind, string Name);

public static class TreasureCatalog
{
    /// <summary>The kind of the Pirate Shop Access prize.</summary>
    public const string PirateShop = "pirate-shop";

    /// <summary>Always null: the companion has no prize list.</summary>
    public static TreasurePrize? Find(string prizeId) => null;

    public static string KindName(string kind) => kind;
}
