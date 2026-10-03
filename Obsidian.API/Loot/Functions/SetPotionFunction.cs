using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Sets the potion of the stack's potion contents, keeping any custom color and effects.
/// </summary>
[LootType("minecraft:set_potion")]
public sealed class SetPotionFunction : LootFunction
{
    public required Potion Id { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        var contents = stack.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
        LootItems.Set(stack, contents is null ? new PotionContentsDataComponent { Potion = this.Id } : contents with { Potion = this.Id });
        return stack;
    }
}
