// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
namespace Jotanator;

public enum ItemKind
{
    /// <summary>Used up (e.g. by the next daily).</summary>
    Consumable,
    /// <summary>Collectible shown next to your name; one of each.</summary>
    Badge,
    /// <summary>Unlocks something permanently; one of each.</summary>
    Perk,
}

public sealed record ShopItem(string Id, string Name, string Emoji, long BasePrice, ItemKind Kind, string Description);

/// <summary>The shop catalogue. Real prices are BasePrice x the current price index (see j.inflation).</summary>
public static class Shop
{
    public static readonly ShopItem[] Items =
    [
        new("charm", "Lucky Charm", "🍀", 800, ItemKind.Consumable, "Doubles your next `daily`."),
        new("title", "Custom Title", "🏷️", 2500, ItemKind.Perk, "Lets you set a title shown on your profile (`title <text>`)."),
        new("star", "Star Badge", "⭐", 1500, ItemKind.Badge, "A badge shown next to your name."),
        new("crown", "Crown Badge", "👑", 6000, ItemKind.Badge, "A fancier badge shown next to your name."),
        new("gem", "Gem Badge", "💎", 20000, ItemKind.Badge, "The flashiest badge there is."),
    ];

    public static ShopItem? Find(string id) =>
        Items.FirstOrDefault(i => string.Equals(i.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<ShopItem> Badges => Items.Where(i => i.Kind == ItemKind.Badge);
}
