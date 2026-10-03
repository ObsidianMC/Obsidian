using System.Collections.Frozen;

namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Swaps stone and cobblestone blocks for their blackstone counterparts, keeping stair and slab shapes, like vanilla's
/// <c>BlackstoneReplaceProcessor</c> (nether ruined portals).
/// </summary>
[ConfiguredFeatureProperty("minecraft:blackstone_replace")]
public sealed class BlackstoneReplaceProcessor : StructureProcessor
{
    public static BlackstoneReplaceProcessor Instance { get; } = new() { Type = "minecraft:blackstone_replace" };

    private static readonly FrozenDictionary<string, string> replacements = new Dictionary<string, string>
    {
        ["minecraft:cobblestone"] = "minecraft:blackstone",
        ["minecraft:mossy_cobblestone"] = "minecraft:blackstone",
        ["minecraft:stone"] = "minecraft:polished_blackstone",
        ["minecraft:stone_bricks"] = "minecraft:polished_blackstone_bricks",
        ["minecraft:mossy_stone_bricks"] = "minecraft:polished_blackstone_bricks",
        ["minecraft:cobblestone_stairs"] = "minecraft:blackstone_stairs",
        ["minecraft:mossy_cobblestone_stairs"] = "minecraft:blackstone_stairs",
        ["minecraft:stone_stairs"] = "minecraft:polished_blackstone_stairs",
        ["minecraft:stone_brick_stairs"] = "minecraft:polished_blackstone_brick_stairs",
        ["minecraft:mossy_stone_brick_stairs"] = "minecraft:polished_blackstone_brick_stairs",
        ["minecraft:cobblestone_slab"] = "minecraft:blackstone_slab",
        ["minecraft:mossy_cobblestone_slab"] = "minecraft:blackstone_slab",
        ["minecraft:smooth_stone_slab"] = "minecraft:polished_blackstone_slab",
        ["minecraft:stone_slab"] = "minecraft:polished_blackstone_slab",
        ["minecraft:stone_brick_slab"] = "minecraft:polished_blackstone_brick_slab",
        ["minecraft:mossy_stone_brick_slab"] = "minecraft:polished_blackstone_brick_slab",
        ["minecraft:stone_brick_wall"] = "minecraft:polished_blackstone_brick_wall",
        ["minecraft:mossy_stone_brick_wall"] = "minecraft:polished_blackstone_brick_wall",
        ["minecraft:cobblestone_wall"] = "minecraft:blackstone_wall",
        ["minecraft:mossy_cobblestone_wall"] = "minecraft:blackstone_wall",
        ["minecraft:chiseled_stone_bricks"] = "minecraft:chiseled_polished_blackstone",
        ["minecraft:cracked_stone_bricks"] = "minecraft:cracked_polished_blackstone_bricks",
        ["minecraft:iron_bars"] = "minecraft:iron_chain"
    }.ToFrozenDictionary();

    public override StructureBlockInfo? ProcessBlock(IWorldGenLevel level, Vector origin, Vector pivot, StructureBlockInfo original,
        StructureBlockInfo current, StructurePlaceSettings settings)
    {
        if (!replacements.TryGetValue(current.Block.UnlocalizedName, out var replacement))
            return current;

        // Only the stair and slab properties carry over; walls and chains get their default state.
        var state = BlockStateProperties.GetState(replacement);
        foreach (var property in (ReadOnlySpan<string>)["facing", "half", "type"])
        {
            var value = current.Block.GetProperty(property);
            if (value is not null)
                state = state.WithProperty(property, value);
        }

        return current with { Block = state };
    }
}
