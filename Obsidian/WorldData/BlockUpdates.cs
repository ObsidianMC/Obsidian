using Obsidian.API.BlockStates;
using Obsidian.API.BlockStates.Builders;
using Obsidian.Registries;

namespace Obsidian.WorldData;

internal static class BlockUpdates
{
    internal static async Task<bool> HandleFallingBlock(BlockUpdate blockUpdate)
    {
        if (blockUpdate.Block is null) { return false; }

        var world = blockUpdate.world;
        var location = blockUpdate.position;
        var material = blockUpdate.Block.Material;
        if (await world.GetBlockAsync(location + Vector.Down) is IBlock below &&
            (TagsRegistry.Blocks.ReplaceableByLiquid.Entries.Contains(below.RegistryId) || below.IsLiquid))
        {
            world.SpawnFallingBlock(location, material);
        }

        return false;
    }

    internal static async Task<bool> HandleLiquidPhysicsAsync(BlockUpdate blockUpdate)
    {
        if (blockUpdate.Block is null) { return false; }

        var block = blockUpdate.Block;
        var world = blockUpdate.world;
        var location = blockUpdate.position;
        int liquidLevel = GetLiquidState(block);
        Vector belowPos = location + Vector.Down;

        // Handle the initial search for closet path downwards.
        // Just going to do a crappy pathfind for now. We can do
        // proper pathfinding some other time.
        if (liquidLevel == 0)
        {
            var validPaths = new List<Vector>();
            var paths = new List<Vector>() {
                    {location + Vector.Forwards},
                    {location + Vector.Backwards},
                    {location + Vector.Left},
                    {location + Vector.Right}
                };

            foreach (var pathLoc in paths)
            {
                if (await world.GetBlockAsync(pathLoc) is IBlock pathSide &&
                    (TagsRegistry.Blocks.ReplaceableByLiquid.Entries.Contains(pathSide.RegistryId) || pathSide.IsLiquid))
                {
                    var pathBelow = await world.GetBlockAsync(pathLoc + Vector.Down);
                    if (pathBelow is IBlock pBelow &&
                        (TagsRegistry.Blocks.ReplaceableByLiquid.Entries.Contains(pBelow.RegistryId) || pBelow.IsLiquid))
                    {
                        validPaths.Add(pathLoc);
                    }
                }
            }

            // if all directions are valid, or none are, use normal liquid spread physics instead
            if (validPaths.Count != 4 && validPaths.Count != 0)
            {
                var path = validPaths[0];
                var newBlock = BlocksRegistry.Get(block.Material, DetermineBlockState(block.Material, liquidLevel + 1));
                await world.SetBlockAsync(path, newBlock);
                var neighborUpdate = new BlockUpdate(world, path, newBlock);
                await world.ScheduleBlockUpdateAsync(neighborUpdate);
                return false;
            }
        }

        if (liquidLevel >= 8) // Falling water
        {
            // If above me is no longer water, than I should disappear too
            if (await world.GetBlockAsync(location + Vector.Up) is IBlock up && !up.IsLiquid)
            {
                await world.SetBlockAsync(location, BlocksRegistry.Air);
                await world.ScheduleBlockUpdateAsync(new BlockUpdate(world, belowPos));
                return false;
            }

            // Keep falling
            if (await world.GetBlockAsync(belowPos) is IBlock below && TagsRegistry.Blocks.ReplaceableByLiquid.Entries.Contains(below.RegistryId))
            {
                var newBlock = BlocksRegistry.Get(block.Material, DetermineBlockState(block.Material, liquidLevel));
                await world.SetBlockAsync(belowPos, newBlock);
                await world.ScheduleBlockUpdateAsync(new BlockUpdate(world, belowPos, newBlock));
                return false;
            }
            else
            {
                // Falling water has hit something solid. Change state to spread.
                liquidLevel = 1;
                await world.SetBlockUntrackedAsync(location, BlocksRegistry.Get(block.Material, DetermineBlockState(block.Material, liquidLevel)));
            }
        }

        if (liquidLevel < 8)
        {
            var horizontalNeighbors = new Dictionary<Vector, IBlock?>() {
                    {location + Vector.Forwards, await world.GetBlockAsync(location + Vector.Forwards)},
                    {location + Vector.Backwards, await world.GetBlockAsync(location + Vector.Backwards)},
                    {location + Vector.Left, await world.GetBlockAsync(location + Vector.Left)},
                    {location + Vector.Right, await world.GetBlockAsync(location + Vector.Right)}
                };

            // Check infinite source blocks
            if (liquidLevel == 1 && block.IsLiquid)
            {
                // if 2 neighbors are source blocks (state = 0), then become source block
                int sourceNeighborCount = 0;
                foreach (var (loc, neighborBlock) in horizontalNeighbors)
                {
                    if (neighborBlock is IBlock neighbor && neighbor.Is(block.Material) && (GetLiquidState(neighbor) == 0))
                    {
                        sourceNeighborCount++;
                    }
                }

                if (sourceNeighborCount > 1)
                {
                    await world.SetBlockAsync(location, BlocksRegistry.Get(block.Material));//Lava shouldn't have infinite source
                    return true;
                }
            }

            if (liquidLevel > 0)
            {
                // On some side of the block, there should be another water block with a lower state.
                int lowestState = liquidLevel;
                foreach (var (loc, neighborBlock) in horizontalNeighbors)
                {
                    var neighborState = neighborBlock.IsLiquid ? GetLiquidState(neighborBlock) : 0; 

                    if (neighborBlock.Material == block.Material)
                        lowestState = Math.Min(lowestState, neighborState);
                }

                // If not, turn to air and update neighbors.
                var bUp = await world.GetBlockAsync(location + Vector.Up);
                if (lowestState >= liquidLevel && bUp.Material != block.Material)
                {
                    await world.SetBlockAsync(location, BlocksRegistry.Air);
                    return true;
                }
            }

            if (await world.GetBlockAsync(belowPos) is IBlock below)
            {
                if (below.Material == block.Material) { return false; }

                if (TagsRegistry.Blocks.ReplaceableByLiquid.Entries.Contains(below.RegistryId))
                {
                    var newBlock = BlocksRegistry.Get(block.Material, DetermineBlockState(block.Material, liquidLevel + 8));
                    await world.SetBlockAsync(belowPos, newBlock);
                    var neighborUpdate = new BlockUpdate(world, belowPos, newBlock);
                    await world.ScheduleBlockUpdateAsync(neighborUpdate);
                    return false;
                }
            }

            // the lowest level of water can only go down, so bail now.
            if (liquidLevel == 7) { return false; }

            foreach (var (loc, neighborBlock) in horizontalNeighbors)
            {
                if (neighborBlock is null) { continue; }

                var neighborState = neighborBlock.State is WaterState neighborWater ? neighborWater.Level : 0;

                if (TagsRegistry.Blocks.ReplaceableByLiquid.Entries.Contains(neighborBlock.RegistryId) ||
                    (neighborBlock.IsLiquid && neighborState > liquidLevel + 1))
                {
                    var newBlock = BlocksRegistry.Get(block.Material, DetermineBlockState(block.Material, liquidLevel + 1));
                    await world.SetBlockAsync(loc, newBlock);
                    var neighborUpdate = new BlockUpdate(world, loc, newBlock);
                    await world.ScheduleBlockUpdateAsync(neighborUpdate);
                }
            }
        }

        return false;
    }

    private static int GetLiquidState(IBlock block)
    {
        if (block.Is(Material.Lava))
            return block.State is LavaState lava ? lava.Level : 0;

        return block.State is WaterState water ? water.Level : 0;
    }

    private static IBlockState DetermineBlockState(Material material, int newLevel)
    {
        if (material is Material.Lava)
            return new LavaStateBuilder().WithLevel(newLevel).Build();

        return new WaterStateBuilder().WithLevel(newLevel).Build();
    }
}
