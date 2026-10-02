using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Sets the potion of the stack's potion contents, keeping any custom color and effects.
/// </summary>
[LootType("minecraft:set_potion")]
public sealed class SetPotionFunction : LootFunction
{
    /// <summary>
    /// The potion id, e.g. <c>minecraft:water_breathing</c>.
    /// </summary>
    public required string Id { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        var contents = stack.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
        var potion = new Potion { Name = this.Id, Effects = [] };

        LootItems.Set(stack, contents is null ? new PotionContentsDataComponent { Potion = potion, CustomEffects = [] } : contents with { Potion = potion });
        return stack;
    }
}
