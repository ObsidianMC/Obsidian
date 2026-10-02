using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Gives the stack an instrument picked at random from <see cref="Options"/>.
/// </summary>
[LootType("minecraft:set_instrument")]
public sealed class SetInstrumentFunction : LootFunction
{
    /// <summary>
    /// The instruments of an instrument tag, in tag order.
    /// </summary>
    public required InstrumentDefinition[] Options { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        if (this.Options.Length == 0)
            return stack;

        var instrument = this.Options[context.Random.NextInt(this.Options.Length)];
        LootItems.Set(stack, ComponentBuilder.Instrument with { Value = instrument.ToInstrumentData() });
        return stack;
    }
}
