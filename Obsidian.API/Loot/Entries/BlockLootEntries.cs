using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot.Conditions;
using Obsidian.API.Registries;

namespace Obsidian.API.Loot.Entries;

/// <summary>
/// Expands only the first child, in order, whose conditions pass, like vanilla's <c>AlternativesEntry</c>.
/// </summary>
[LootType("minecraft:alternatives")]
public sealed class AlternativesEntry : LootPoolEntry
{
    public required ImmutableArray<LootPoolEntry> Children { get; init; }

    public override bool Expand(LootContext context, Action<LootPoolSingletonEntry> output)
    {
        if (!ILootCondition.TestAll(this.Conditions, context))
            return false;

        foreach (var child in this.Children)
        {
            if (child.Expand(context, output))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Generates the stacks a named drop provider (e.g. <c>minecraft:sherds</c>) produces for the block.
/// </summary>
[LootType("minecraft:dynamic")]
public sealed class DynamicEntry : LootPoolSingletonEntry
{
    public required string Name { get; init; }

    protected override void CreateItems(Action<ItemStack> output, LootContext context)
    {
        if (this.Name != "minecraft:sherds")
            throw new NotSupportedException($"The dynamic drop {this.Name} isn't supported yet.");

        // Vanilla yields the pot's four sides in order (back, left, right, front), a brick for each empty side. The sides
        // are the block entity's pot_decorations component, which is absent when every side is empty.
        var lookup = context.BlockEntityComponent
            ?? throw new NotSupportedException("minecraft:sherds needs the decorated pot's block entity.");
        var sides = (lookup("minecraft:pot_decorations") as SimpleDataComponent<Item[]>)?.Value;
        for (var side = 0; side < 4; side++)
            output(new ItemStack(sides?[side] ?? ItemsRegistry.Brick));
    }
}
