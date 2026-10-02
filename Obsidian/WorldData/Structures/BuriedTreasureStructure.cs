using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A chest buried in the first stone-like block under the ocean floor at block (9, 9) of the chunk, like vanilla's
/// <c>BuriedTreasureStructure</c>.
/// </summary>
[StructureType("minecraft:buried_treasure")]
public sealed class BuriedTreasureStructure : Structure
{
    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context) =>
        OnTopOfChunkCenter(context, HeightmapType.OceanFloorWG, builder =>
            builder.AddPiece(new BuriedTreasurePiece(new Vector((context.ChunkX << 4) + 9, 90, (context.ChunkZ << 4) + 9))));
}

/// <summary>
/// Vanilla's <c>BuriedTreasurePieces.BuriedTreasurePiece</c>.
/// </summary>
public sealed class BuriedTreasurePiece(Vector position) : StructurePiece(0, new BlockBox(position, position))
{
    private static readonly IBlock[] buryingBlocks =
    [
        BlocksRegistry.Get(Material.Sandstone), BlocksRegistry.Get(Material.Stone), BlocksRegistry.Get(Material.Andesite),
        BlocksRegistry.Get(Material.Granite), BlocksRegistry.Get(Material.Diorite)
    ];

    public override void PostProcess(StructurePieceContext context)
    {
        var level = context.Level;
        var position = new Vector(this.BoundingBox.MinX, level.GetHeight(HeightmapType.OceanFloorWG, this.BoundingBox.MinX, this.BoundingBox.MinZ),
            this.BoundingBox.MinZ);

        while (position.Y > level.MinY)
        {
            var block = level.GetBlock(position);
            var below = level.GetBlock(position + Vector.Down);
            if (!buryingBlocks.Any(below.IsSameState))
            {
                position += Vector.Down;
                continue;
            }

            var cover = !block.IsAir && !IsDefaultLiquid(block) ? block : BlocksRegistry.Get(Material.Sand);
            foreach (var face in FeatureHelpers.Directions)
            {
                var neighbor = position.Offset(face);
                var neighborBlock = level.GetBlock(neighbor);
                if (!neighborBlock.IsAir && !IsDefaultLiquid(neighborBlock))
                    continue;

                var underNeighbor = level.GetBlock(neighbor + Vector.Down);
                level.SetBlock(neighbor, (underNeighbor.IsAir || IsDefaultLiquid(underNeighbor)) && face != BlockFace.Up ? below : cover);
            }

            // Like vanilla, the piece shrinks to the chest.
            this.BoundingBox = new BlockBox(position, position);
            this.CreateChest(level, context.Box, context.Random, position, "minecraft:chests/buried_treasure", null);
            return;
        }
    }

    private static bool IsDefaultLiquid(IBlock block) =>
        block.IsSameState(BlocksRegistry.Get(Material.Water)) || block.IsSameState(BlocksRegistry.Get(Material.Lava));
}
