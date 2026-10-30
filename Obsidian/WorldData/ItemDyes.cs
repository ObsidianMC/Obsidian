using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using System.Collections.Frozen;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>
/// The <c>minecraft:dye</c> item component (26.1), the color an item dyes things: a stack's own when it sets one, none
/// when it removes it, and its item's default otherwise (from <c>item_components.json</c>; vanilla's dyes have one).
/// </summary>
internal static class ItemDyes
{
    private static readonly Lazy<FrozenDictionary<string, Dye>> defaults = new(LoadDefaults);

    // Vanilla's DyeColor texture diffuse colors (RGB), which dyeing mixes, by dye.
    private static readonly int[] textureColors =
    [
        0xF9FFFE, 0xF9801D, 0xC74EBD, 0x3AB3DA, 0xFED83D, 0x80C71F, 0xF38BAA, 0x474F52,
        0x9D9D97, 0x169C9C, 0x8932B8, 0x3C44AA, 0x835432, 0x5E7C16, 0xB02E26, 0x1D1D21
    ];

    /// <summary>The stack's dye color, or <c>null</c> if it isn't a dye.</summary>
    public static Dye? Of(ItemStack item)
    {
        if (item.IsAir || item.RemoveComponents.Contains(DataComponentType.Dye))
            return null;

        var own = item.GetComponent<SimpleDataComponent<Dye>>(DataComponentType.Dye);
        if (own is not null)
            return own.Value;

        return defaults.Value.TryGetValue(item.Holder.UnlocalizedName, out var dye) ? dye : null;
    }

    /// <summary>The RGB color <paramref name="dye"/> tints with.</summary>
    public static int TextureColor(Dye dye) => textureColors[(int)dye];

    private static FrozenDictionary<string, Dye> LoadDefaults()
    {
        var assembly = typeof(ItemDyes).Assembly;
        using var stream = assembly.GetManifestResourceStream("Obsidian.Assets.item_components.json")!;
        using var document = JsonDocument.Parse(stream);

        var dyes = new Dictionary<string, Dye>();
        foreach (var item in document.RootElement.EnumerateObject())
        {
            if (!item.Value.GetProperty("components").TryGetProperty("minecraft:dye", out var dye))
                continue;

            var name = dye.GetString()!.Replace("_", "");
            dyes.Add(item.Name, Enum.Parse<Dye>(name, ignoreCase: true));
        }

        return dyes.ToFrozenDictionary();
    }
}
