using Obsidian.API.Inventory;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The blocks redstone items place when the item isn't named like its block: redstone dust from <c>minecraft:redstone</c>.
/// </summary>
internal static class VanillaRedstone
{
    /// <summary>
    /// The block an item places that isn't named like it: redstone dust from <c>minecraft:redstone</c>.
    /// </summary>
    public static bool TryGetPlacedBlock(Item item, [NotNullWhen(true)] out IBlock? block)
    {
        block = item.Id == ItemIds.Redstone ? BlocksRegistry.RedstoneWire : null;
        return block is not null;
    }
}
