namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Keeps lava where a template block whose shape isn't a full cube would replace it, like vanilla's
/// <c>LavaSubmergedBlockProcessor</c> (nether ruined portals).
/// </summary>
[ConfiguredFeatureProperty("minecraft:lava_submerged_block")]
public sealed class LavaSubmergedBlockProcessor : StructureProcessor
{
    public static LavaSubmergedBlockProcessor Instance { get; } = new() { Type = "minecraft:lava_submerged_block" };

    private static readonly IBlock lava = BlocksRegistry.Get(Material.Lava);

    public override StructureBlockInfo? ProcessBlock(IWorldGenLevel level, Vector origin, Vector pivot, StructureBlockInfo original,
        StructureBlockInfo current, StructurePlaceSettings settings) =>
        level.GetBlock(current.Position).Material == Material.Lava && !StructureBlockShapes.IsShapeFullBlock(current.Block)
            ? current with { Block = lava }
            : current;
}
