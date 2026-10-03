using Obsidian.Nbt;

namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Turns jigsaw blocks into their <c>final_state</c> (air by default; structure voids are skipped), like vanilla's
/// <c>JigsawReplacementProcessor</c>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:jigsaw_replacement")]
public sealed class JigsawReplacementProcessor : StructureProcessor
{
    public static JigsawReplacementProcessor Instance { get; } = new() { Type = "minecraft:jigsaw_replacement" };

    public override StructureBlockInfo? ProcessBlock(IWorldGenLevel level, Vector origin, Vector pivot, StructureBlockInfo original,
        StructureBlockInfo current, StructurePlaceSettings settings)
    {
        if (current.Block.Material != Material.Jigsaw || current.Nbt is null)
            return current;

        var finalState = current.Nbt.TryGetTag<NbtTag<string>>("final_state", out var tag) ? tag.Value! : "minecraft:air";
        var block = BlockStateParser.TryParse(finalState);
        if (block is null || block.Material == Material.StructureVoid)
            return null;

        return new StructureBlockInfo(current.Position, block, null);
    }
}
