using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Weathers stone brick structures: cracks or moss stone bricks, breaks stairs into slabs, mosses slabs and walls and turns
/// obsidian crying, like vanilla's <c>BlockAgeProcessor</c> (ruined portals).
/// </summary>
/// <remarks>Draws from <see cref="StructurePlaceSettings.GetRandom"/> in vanilla's order.</remarks>
[ConfiguredFeatureProperty("minecraft:block_age")]
public sealed class BlockAgeProcessor : StructureProcessor
{
    private static readonly BlockSet fullStone = new("minecraft:stone_bricks", "minecraft:stone", "minecraft:chiseled_stone_bricks");
    private static readonly BlockSet stairs = new("#minecraft:stairs");
    private static readonly BlockSet slabs = new("#minecraft:slabs");
    private static readonly BlockSet walls = new("#minecraft:walls");

    private static readonly IBlock[] nonMossyReplacements =
        [BlockStateProperties.GetState("minecraft:stone_slab"), BlockStateProperties.GetState("minecraft:stone_brick_slab")];

    // The properties stairs, slabs and walls share with their mossy versions (vanilla's withPropertiesOf).
    private static readonly string[] copiedProperties = ["facing", "half", "shape", "type", "waterlogged", "up", "north", "east", "south", "west"];

    /// <summary>The chance for replacements to be mossy rather than cracked or broken.</summary>
    public required float Mossiness { get; init; }

    public override StructureBlockInfo? ProcessBlock(IWorldGenLevel level, Vector origin, Vector pivot, StructureBlockInfo original,
        StructureBlockInfo current, StructurePlaceSettings settings)
    {
        var random = settings.GetRandom(current.Position);
        var block = current.Block;
        IBlock? replacement = null;

        if (fullStone.Contains(block))
            replacement = this.MaybeReplaceFullStoneBlock(random);
        else if (stairs.Contains(block))
            replacement = this.MaybeReplaceStairs(block, random);
        else if (slabs.Contains(block))
            replacement = random.NextFloat() < this.Mossiness ? WithPropertiesOf("minecraft:mossy_stone_brick_slab", block) : null;
        else if (walls.Contains(block))
            replacement = random.NextFloat() < this.Mossiness ? WithPropertiesOf("minecraft:mossy_stone_brick_wall", block) : null;
        else if (block.Material == Material.Obsidian)
            replacement = random.NextFloat() < 0.15f ? BlockStateProperties.GetState("minecraft:crying_obsidian") : null;

        return replacement is null ? current : current with { Block = replacement };
    }

    private IBlock? MaybeReplaceFullStoneBlock(IRandomSource random)
    {
        if (random.NextFloat() >= 0.5f)
            return null;

        IBlock[] cracked = [BlockStateProperties.GetState("minecraft:cracked_stone_bricks"), RandomFacingStairs(random, "minecraft:stone_brick_stairs")];
        IBlock[] mossy = [BlockStateProperties.GetState("minecraft:mossy_stone_bricks"), RandomFacingStairs(random, "minecraft:mossy_stone_brick_stairs")];
        return this.GetRandomBlock(random, cracked, mossy);
    }

    private IBlock? MaybeReplaceStairs(IBlock block, IRandomSource random)
    {
        if (random.NextFloat() >= 0.5f)
            return null;

        IBlock[] mossy = [WithPropertiesOf("minecraft:mossy_stone_brick_stairs", block), BlockStateProperties.GetState("minecraft:mossy_stone_brick_slab")];
        return this.GetRandomBlock(random, nonMossyReplacements, mossy);
    }

    private IBlock GetRandomBlock(IRandomSource random, IBlock[] nonMossy, IBlock[] mossy)
    {
        var choices = random.NextFloat() < this.Mossiness ? mossy : nonMossy;
        return choices[random.NextInt(choices.Length)];
    }

    /// <summary>Stairs facing a random horizontal direction (<c>nextInt(4)</c>), top or bottom (<c>nextInt(2)</c>).</summary>
    private static IBlock RandomFacingStairs(IRandomSource random, string name)
    {
        var facing = FeatureHelpers.FaceName(FeatureHelpers.RandomHorizontal(random));
        var half = random.NextInt(2) == 0 ? "top" : "bottom";
        return BlockStateProperties.GetState(name).WithProperty("facing", facing).WithProperty("half", half);
    }

    private static IBlock WithPropertiesOf(string name, IBlock source)
    {
        var state = BlockStateProperties.GetState(name);
        foreach (var property in copiedProperties)
        {
            var value = source.GetProperty(property);
            if (value is not null)
                state = state.WithProperty(property, value);
        }

        return state;
    }
}
