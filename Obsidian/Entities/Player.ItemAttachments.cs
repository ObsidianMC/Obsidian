using Obsidian.API.Data;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.Entities;

// Plugins' item attachments (see ItemAttachments), like a player's accessory slots, at the player's death.
public partial class Player
{
    /// <summary>
    /// Drops the items of the player's item attachments a death doesn't keep, like vanilla's
    /// <c>Player.dropEquipment</c> drops the inventory: when the <c>keep_inventory</c> rule is off, the attachments
    /// registered without <c>copyOnDeath</c> are emptied, their items with the curse of vanishing destroyed and the
    /// others dropped around the player.
    /// </summary>
    /// <remarks>
    /// Obsidian doesn't drop the player's own inventory at a death yet (see known-gaps.md).
    /// </remarks>
    private void DropItemAttachments()
    {
        if (this.Level.LevelData.GetBooleanRule("keep_inventory", defaultValue: false))
            return;

        foreach (var item in AttachmentStorage.For(this).TakeDeathDrops(this))
        {
            if (!Vanishes(item))
                ItemEntity.DropAround(this, item);
        }
    }

    // Vanilla's destroyVanishingCursedItems: an item enchanted with the curse of vanishing is lost at a death.
    private static bool Vanishes(ItemStack item) =>
        item.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(DataComponentType.Enchantments)?.Value
            .Any(entry => entry.Id == EnchantmentsRegistry.VanishingCurse.Id) == true;
}
