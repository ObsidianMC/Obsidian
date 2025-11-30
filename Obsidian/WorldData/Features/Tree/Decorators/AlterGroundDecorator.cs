using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using Obsidian.Registries;

namespace Obsidian.WorldData.Features.Tree.Decorators;

/// <summary>
/// Decorator that alters ground blocks around the base of a tree (e.g., grass to podzol for large spruce trees).
/// </summary>
[TreeProperty("minecraft:alter_ground")]
public sealed class AlterGroundDecorator : DecoratorBase
{
    public override string Type { get; init; } = "minecraft:alter_ground";

    public required IBlockStateProvider Provider { get; set; }

    public override async ValueTask Place(FeatureContext context, List<Vector> trunkPositions, List<Vector> foliagePositions, List<Vector> rootPositions)
    {
        // Combine trunk and root positions to find the true base
        var allBasePositions = trunkPositions.Concat(rootPositions).ToList();

        if (allBasePositions.Count == 0)
            return;

        var world = context.World;
        var random = context.Random;

        // Find the lowest Y position (base of tree)
        int minY = allBasePositions.Min(pos => pos.Y);
        var basePositions = allBasePositions.Where(pos => pos.Y == minY).ToList();        // Place circles around each base position
        foreach (var basePos in basePositions)
        {
            // Place circles at the four corners of a 3x3 area
            await PlaceCircle(world, basePos + (-1, 0, -1)); // West-North
            await PlaceCircle(world, basePos + (2, 0, -1));  // East-North
            await PlaceCircle(world, basePos + (-1, 0, 2));  // West-South
            await PlaceCircle(world, basePos + (2, 0, 2));   // East-South

            // Place 5 additional random circles around the perimeter of a 7x7 area
            for (int i = 0; i < 5; i++)
            {
                int placement = random.Next(64);
                int xx = placement % 8;
                int zz = placement / 8;
                // Only place on the outer edge (border of 8x8 grid)
                if (xx == 0 || xx == 7 || zz == 0 || zz == 7)
                {
                    await PlaceCircle(world, basePos + (-3 + xx, 0, -3 + zz));
                }
            }
        }
    }

    /// <summary>
    /// Places a circle of blocks (5x5 area excluding corners) at the given position.
    /// </summary>
    private async ValueTask PlaceCircle(IWorld world, Vector centerPos)
    {
        for (int xx = -2; xx <= 2; xx++)
        {
            for (int zz = -2; zz <= 2; zz++)
            {
                // Skip corners (creates circular shape)
                if (Math.Abs(xx) != 2 || Math.Abs(zz) != 2)
                {
                    await PlaceBlockAt(world, centerPos + (xx, 0, zz));
                }
            }
        }
    }

    /// <summary>
    /// Places a block at the given position, searching vertically for a suitable grass or dirt block.
    /// </summary>
    private async ValueTask PlaceBlockAt(IWorld world, Vector pos)
    {
        // Search from 2 blocks above down to 3 blocks below
        for (int dy = 2; dy >= -3; dy--)
        {
            var checkPos = pos + (0, dy, 0);
            var block = await world.GetBlockAsync(checkPos);

            // Check if it's grass or dirt
            if (IsGrassOrDirt(block))
            {
                var replacementBlock = Provider.Get();
                await world.SetBlockAsync(checkPos, replacementBlock);
                break;
            }

            // If we hit a non-air block below ground level, stop searching
            if (!block.IsAir && dy < 0)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Checks if the block is grass or dirt that can be replaced.
    /// </summary>
    private static bool IsGrassOrDirt(IBlock block)
    {
        return TagsRegistry.Block.Dirt.Entries.Contains(block.RegistryId);
    }
}
