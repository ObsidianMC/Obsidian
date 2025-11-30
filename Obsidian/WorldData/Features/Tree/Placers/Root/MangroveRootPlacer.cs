using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using Obsidian.Registries;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Root;

/// <summary>
/// Root placer for mangrove trees with complex branching root systems that grow outward and downward.
/// </summary>
[TreeProperty("minecraft:mangrove_root_placer")]
public sealed class MangroveRootPlacer : RootPlacer
{
    public override required string Type { get; init; }

    public required MangrovePlacement MangroveRootPlacement { get; set; }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, Vector origin, Vector trunkOrigin)
    {
        var world = context.World;
        var random = context.Random;
        var rootPositions = new List<Vector>();

        // Verify we can place roots in the column from origin to trunk
        var columnPos = origin;
        while (columnPos.Y < trunkOrigin.Y)
        {
            if (!await CanPlaceRoot(world, columnPos))
                return rootPositions; // Return empty list on failure

            columnPos = columnPos.Relative(Vector.Up);
        }

        // Add the position directly below trunk
        rootPositions.Add(trunkOrigin.Relative(Vector.Down));

        // Simulate roots in all four horizontal directions
        foreach (var direction in new[] { Vector.North, Vector.South, Vector.East, Vector.West })
        {
            var pos = trunkOrigin + direction;
            var positionsInDirection = new List<Vector>();

            if (!await SimulateRoots(world, random, pos, direction, trunkOrigin, positionsInDirection, 0))
                return rootPositions; // Return positions placed so far on failure

            rootPositions.AddRange(positionsInDirection);
            rootPositions.Add(trunkOrigin + direction);
        }

        // Place all the root blocks
        foreach (var rootPos in rootPositions)
        {
            await PlaceRoot(world, rootPos, RootProvider, MangroveRootPlacement.MuddyRootsProvider);
        }

        return rootPositions;
    }

    private async ValueTask<bool> SimulateRoots(
        IWorld world,
        Random random,
        Vector rootPos,
        Vector direction,
        Vector rootOrigin,
        List<Vector> rootPositions,
        int layer)
    {
        int maxRootLength = MangroveRootPlacement.MaxRootLength;

        if (layer >= maxRootLength || rootPositions.Count > maxRootLength)
            return false;

        foreach (var pos in GetPotentialRootPositions(rootPos, direction, random, rootOrigin))
        {
            if (await CanPlaceRoot(world, pos))
            {
                rootPositions.Add(pos);
                if (!await SimulateRoots(world, random, pos, direction, rootOrigin, rootPositions, layer + 1))
                    return false;
            }
        }

        return true;
    }

    private List<Vector> GetPotentialRootPositions(Vector pos, Vector prevDir, Random random, Vector rootOrigin)
    {
        var below = pos.Relative(Vector.Down);
        var nextTo = pos + prevDir;
        // Manhattan distance: |x1-x2| + |y1-y2| + |z1-z2|
        int width = Math.Abs(pos.X - rootOrigin.X) + Math.Abs(pos.Y - rootOrigin.Y) + Math.Abs(pos.Z - rootOrigin.Z);
        int maxRootWidth = MangroveRootPlacement.MaxRootWidth;
        float randomSkewChance = MangroveRootPlacement.RandomSkewChance;

        if (width > maxRootWidth - 3 && width <= maxRootWidth)
        {
            // Near max width: either go down or continue in direction then down
            return random.NextSingle() < randomSkewChance
                ? new List<Vector> { below, nextTo.Relative(Vector.Down) }
                : new List<Vector> { below };
        }
        else if (width > maxRootWidth)
        {
            // Beyond max width: only go down
            return new List<Vector> { below };
        }
        else if (random.NextSingle() < randomSkewChance)
        {
            // Sometimes just go down early
            return new List<Vector> { below };
        }
        else
        {
            // Either continue in direction or go down
            return random.Next(2) == 0
                ? new List<Vector> { nextTo }
                : new List<Vector> { below };
        }
    }

    private async ValueTask<bool> CanPlaceRoot(IWorld world, Vector pos)
    {
        // Check if position is valid for tree placement
        var block = await world.GetBlockAsync(pos);

        // Can place in air, water, or blocks in the CanGrowThrough list
        if (block.IsAir || block.IsLiquid)
            return true;

        if (MangroveRootPlacement.CanGrowThrough.Contains(block.UnlocalizedName))
            return true;

        // Check if replaceable (like grass, flowers, etc.)
        return TagsRegistry.Block.Replaceable.Entries.Contains(block.RegistryId);
    }

    private async ValueTask PlaceRoot(IWorld world, Vector pos, IBlockStateProvider rootProvider, IBlockStateProvider? muddyProvider)
    {
        var existingBlock = await world.GetBlockAsync(pos);

        // Determine which root type to use
        SimpleBlockState rootState;
        if (muddyProvider != null && MangroveRootPlacement.MuddyRootsIn.Contains(existingBlock.UnlocalizedName))
        {
            rootState = muddyProvider.GetSimple();
        }
        else
        {
            rootState = rootProvider.GetSimple();
        }

        // Handle waterlogging if the block is water
        if (existingBlock.IsLiquid)
        {
            // Set waterlogged property to true
            rootState.Properties["waterlogged"] = "true";
        }

        var rootBlock = BlocksRegistry.GetFromSimpleState(rootState);
        await world.SetBlockAsync(pos, rootBlock);

        // Place a block above the root for support
        var abovePos = pos + Vector.Up;
        var aboveBlock = await world.GetBlockAsync(abovePos);
        if (aboveBlock.IsAir)
        {
            var aboveRootState = rootProvider.GetSimple();
            if (existingBlock.IsLiquid)
            {
                aboveRootState.Properties["waterlogged"] = "true";
            }
            var aboveRootBlock = BlocksRegistry.GetFromSimpleState(aboveRootState);
            await world.SetBlockAsync(abovePos, aboveRootBlock);
        }
    }

    public sealed class MangrovePlacement
    {
        [Range(1, 8)]
        public required int MaxRootWidth { get; set; }

        [Range(1, 15)]
        public required int MaxRootLength { get; set; }

        [Range(0.0, 1.0)]
        public required float RandomSkewChance { get; set; }

        public List<string> CanGrowThrough { get; set; } = [];
        public List<string> MuddyRootsIn { get; set; } = [];

        public required IBlockStateProvider MuddyRootsProvider { get; set; }
    }
}
