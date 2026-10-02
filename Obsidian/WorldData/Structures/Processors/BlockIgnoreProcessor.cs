using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Skips template blocks of the listed block types, like vanilla's <c>BlockIgnoreProcessor</c>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:block_ignore")]
public sealed class BlockIgnoreProcessor : StructureProcessor
{
    /// <summary>Skips structure blocks; every single pool element uses it.</summary>
    public static BlockIgnoreProcessor StructureBlock { get; } = Create("minecraft:structure_block");

    /// <summary>Skips air and structure blocks; legacy single pool elements and most template pieces use it.</summary>
    public static BlockIgnoreProcessor StructureAndAir { get; } = Create("minecraft:air", "minecraft:structure_block");

    /// <summary>Skips air.</summary>
    public static BlockIgnoreProcessor Air { get; } = Create("minecraft:air");

    /// <summary>The blocks to skip; only their block type matters.</summary>
    public required ImmutableArray<SimpleBlockState> Blocks { get; init; }

    private BlockSet Ignored => field ??= new BlockSet([.. this.Blocks.Select(block => block.Name)]);

    public override StructureBlockInfo? ProcessBlock(IWorldGenLevel level, Vector origin, Vector pivot, StructureBlockInfo original,
        StructureBlockInfo current, StructurePlaceSettings settings) =>
        this.Ignored.Contains(current.Block) ? null : current;

    private static BlockIgnoreProcessor Create(params string[] blocks) =>
        new() { Type = "minecraft:block_ignore", Blocks = [.. blocks.Select(name => new SimpleBlockState { Name = name })] };
}
