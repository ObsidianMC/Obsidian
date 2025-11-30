namespace Obsidian.WorldData.Features.Tree.Placers;

/// <summary>
/// Helper methods for foliage placer implementations.
/// </summary>
public static class FoliagePlacerHelper
{
    /// <summary>
    /// Tries to place a leaf block at the specified position.
    /// Checks for persistent leaves and valid tree positions.
    /// </summary>
    /// <param name="world">The world to place in.</param>
    /// <param name="pos">The position to place at.</param>
    /// <param name="foliageBlock">The foliage block to place.</param>
    /// <param name="placedPositions">Optional list to track placed positions.</param>
    /// <returns>True if a block was placed.</returns>
    public static async ValueTask<bool> TryPlaceLeaf(IWorld world, Vector pos, IBlock foliageBlock, List<Vector>? placedPositions = null)
    {
        var existingBlock = await world.GetBlockAsync(pos);
        if (existingBlock == null)
            return false;

        // Don't replace persistent leaves (player-placed or special leaves)
        // TODO: Check for persistent blockstate property when available

        // Check if position is valid for tree placement
        bool canPlace = TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId);

        if (canPlace)
        {
            // TODO: Handle waterlogged property when blockstate system supports it
            await world.SetBlockUntrackedAsync(pos, foliageBlock, false);
            placedPositions?.Add(pos);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Places a horizontal layer of leaves in a square pattern around the origin.
    /// </summary>
    /// <param name="placedPositions">Optional list to track placed positions.</param>
    public static async ValueTask PlaceLeavesRow(
        IWorld world,
        Random random,
        IBlock foliageBlock,
        Vector origin,
        int currentRadius,
        int yOffset,
        bool doubleTrunk,
        Func<Random, int, int, int, int, bool, bool> shouldSkipLocation,
        List<Vector>? placedPositions = null)
    {
        int offset = doubleTrunk ? 1 : 0;

        for (int dx = -currentRadius; dx <= currentRadius + offset; dx++)
        {
            for (int dz = -currentRadius; dz <= currentRadius + offset; dz++)
            {
                if (!ShouldSkipLocationSigned(random, dx, yOffset, dz, currentRadius, doubleTrunk, shouldSkipLocation))
                {
                    var pos = origin + new Vector(dx, yOffset, dz);
                    await TryPlaceLeaf(world, pos, foliageBlock, placedPositions);
                }
            }
        }
    }

    /// <summary>
    /// Places a layer of leaves with hanging leaves below the edges.
    /// Used for cherry trees and similar foliage types.
    /// </summary>
    /// <param name="placedPositions">Optional list to track placed positions.</param>
    public static async ValueTask PlaceLeavesRowWithHangingLeaves(
        IWorld world,
        Random random,
        IBlock foliageBlock,
        Vector origin,
        int currentRadius,
        int yOffset,
        bool doubleTrunk,
        float hangingLeavesChance,
        float hangingLeavesExtensionChance,
        Func<Random, int, int, int, int, bool, bool> shouldSkipLocation,
        List<Vector>? placedPositions = null)
    {
        // Place the main leaves layer
        await PlaceLeavesRow(world, random, foliageBlock, origin, currentRadius, yOffset, doubleTrunk, shouldSkipLocation, placedPositions);

        int offset = doubleTrunk ? 1 : 0;
        var logPos = origin + new Vector(0, -1, 0); // Below origin

        // Place hanging leaves along the edges (4 sides)
        var horizontalDirs = new[]
        {
            new Vector(1, 0, 0),   // East
            new Vector(0, 0, 1),   // South
            new Vector(-1, 0, 0),  // West
            new Vector(0, 0, -1)   // North
        };

        for (int dirIndex = 0; dirIndex < 4; dirIndex++)
        {
            var alongEdge = horizontalDirs[dirIndex];
            var toEdge = horizontalDirs[(dirIndex + 1) % 4]; // Clockwise 90 degrees

            // Calculate offset to the edge
            int offsetToEdge = (toEdge.X > 0 || toEdge.Z > 0) ? currentRadius + offset : currentRadius;

            // Start position: edge of the leaf layer
            var pos = origin + new Vector(0, yOffset - 1, 0) + (toEdge * offsetToEdge) + (alongEdge * -currentRadius);

            // Walk along the edge
            for (int offsetAlongEdge = -currentRadius; offsetAlongEdge < currentRadius + offset; offsetAlongEdge++)
            {
                // Check if there are leaves above this position
                var posAbove = pos + new Vector(0, 1, 0);
                var blockAbove = await world.GetBlockAsync(posAbove);
                bool leavesAbove = blockAbove != null && blockAbove.UnlocalizedName.Contains("leaves");

                if (leavesAbove)
                {
                    // Try to place first hanging leaf
                    if (await TryPlaceExtension(world, random, foliageBlock, hangingLeavesChance, logPos, pos, placedPositions))
                    {
                        // Try to extend one more block down
                        var posBelow = pos + new Vector(0, -1, 0);
                        await TryPlaceExtension(world, random, foliageBlock, hangingLeavesExtensionChance, logPos, posBelow, placedPositions);
                    }
                }

                // Move to next position along edge
                pos += alongEdge;
            }
        }
    }

    /// <summary>
    /// Tries to place a hanging leaf extension with a probability check and distance limit.
    /// </summary>
    private static async ValueTask<bool> TryPlaceExtension(
        IWorld world,
        Random random,
        IBlock foliageBlock,
        float chance,
        Vector logPos,
        Vector pos,
        List<Vector>? placedPositions = null)
    {
        // Check Manhattan distance limit (max 7 blocks from trunk)
        int manhattanDist = Math.Abs(pos.X - logPos.X) + Math.Abs(pos.Y - logPos.Y) + Math.Abs(pos.Z - logPos.Z);
        if (manhattanDist >= 7)
            return false;

        // Probability check
        if (random.NextSingle() > chance)
            return false;

        return await TryPlaceLeaf(world, pos, foliageBlock, placedPositions);
    }

    /// <summary>
    /// Determines if a location should be skipped, handling signed coordinates for 2x2 trunks.
    /// </summary>
    private static bool ShouldSkipLocationSigned(
        Random random,
        int dx,
        int y,
        int dz,
        int currentRadius,
        bool doubleTrunk,
        Func<Random, int, int, int, int, bool, bool> shouldSkipLocation)
    {
        int minDx, minDz;

        if (doubleTrunk)
        {
            // For 2x2 trunks, use minimum distance to either trunk block
            minDx = Math.Min(Math.Abs(dx), Math.Abs(dx - 1));
            minDz = Math.Min(Math.Abs(dz), Math.Abs(dz - 1));
        }
        else
        {
            minDx = Math.Abs(dx);
            minDz = Math.Abs(dz);
        }

        return shouldSkipLocation(random, minDx, y, minDz, currentRadius, doubleTrunk);
    }
}
