using Obsidian.API.World.Structures;

namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Keeps lava where a template block whose shape isn't a full cube would replace it, like vanilla's
/// <c>LavaSubmergedBlockProcessor</c> (nether ruined portals).
/// </summary>
[ConfiguredFeatureProperty("minecraft:lava_submerged_block")]
public sealed class LavaSubmergedBlockProcessor : StructureProcessor
{
    public static LavaSubmergedBlockProcessor Instance { get; } = new() { Type = "minecraft:lava_submerged_block" };

    private static readonly IBlock lava = BlocksRegistry.Lava;

    public override StructureBlockInfo? ProcessBlock(
        IWorldGenLevel level,
        Vector origin,
        Vector pivot,
        StructureBlockInfo original,
        StructureBlockInfo current,
        TemplatePlacement placement) =>
        level.GetBlock(current.Position).Is(BlockIds.Lava) && !StructureBlockShapes.IsShapeFullBlock(current.Block)
            ? current with { Block = lava }
            : current;
}
