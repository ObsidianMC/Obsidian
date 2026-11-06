using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using System.Text.Json;

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
    public ImmutableArray<InstrumentDefinition> Options { get; init; } = [];

    // A data pack's options, resolved as the function runs so that it sees the current tags.
    internal JsonElement? OptionsSet { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        var options = LootTableJson.ReadInstrumentOptions(this.OptionsSet) ?? this.Options;
        if (options.Length == 0)
            return stack;

        var instrument = options[context.Random.NextInt(options.Length)];
        LootItems.Set(stack, ComponentBuilder.Instrument with { Value = instrument.ToInstrumentData() });
        return stack;
    }
}
