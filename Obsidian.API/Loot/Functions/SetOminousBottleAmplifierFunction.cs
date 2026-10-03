using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot.Numbers;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Sets an ominous bottle's amplifier, clamped to 0..4.
/// </summary>
[LootType("minecraft:set_ominous_bottle_amplifier")]
public sealed class SetOminousBottleAmplifierFunction : LootFunction
{
    public required INumberProvider Amplifier { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        var amplifier = Math.Clamp(this.Amplifier.GetInt(context), 0, 4);
        LootItems.Set(stack, ComponentBuilder.OminousBottleAmplifier with { Value = amplifier });
        return stack;
    }
}
