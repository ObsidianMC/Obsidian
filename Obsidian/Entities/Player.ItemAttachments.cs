using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.Entities;

// Plugins' item attachments (see ItemAttachments), like a player's accessory slots, at the player's death.
public partial class Player
{
    // Vanilla's destroyVanishingCursedItems: an item enchanted with the curse of vanishing is lost at a death.
    private static bool Vanishes(ItemStack item) =>
        item.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(DataComponentType.Enchantments)?.Value
            .Any(entry => entry.Id == EnchantmentsRegistry.VanishingCurse.Id) == true;
}
