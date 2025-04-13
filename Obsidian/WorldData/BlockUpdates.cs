using Obsidian.API.BlockStates;
using Obsidian.API.BlockStates.Builders;

namespace Obsidian.WorldData;

internal static class BlockUpdates
{
    /// <summary>
    /// Perform gravity affected block tick logic
    /// </summary>
    /// <param name="blockUpdate">Info about the block update</param>
    /// <returns>Whether caller should block update neighbors</returns>
    internal static async Task<bool> HandleFallingBlock(IBlockUpdate blockUpdate)
    {
        if (blockUpdate.Block is null) { return false; }

        var world = blockUpdate.World;
        var position = blockUpdate.Position;
        var material = blockUpdate.Block.Material;
        if (await world.GetBlockAsync(position + Vector.Down) is IBlock below &&
            (TagsRegistry.Block.ReplaceableByLiquid.Entries.Contains(below.RegistryId) || below.IsLiquid))
        {
            await world.SetBlockAsync(position, BlocksRegistry.Air);
            world.SpawnFallingBlock(position, material);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Perform tick logic for fluids
    /// </summary>
    /// <param name="blockUpdate">Info about the block update</param>
    /// <returns>Whether caller should block update neighbors</returns>
    internal static async Task<bool> HandleLiquidPhysicsAsync(IBlockUpdate blockUpdate)
    {
        if (blockUpdate.Block is null) { return false; }

        var block = blockUpdate.Block;
        var world = blockUpdate.World;
        var position = blockUpdate.Position;
        int liquidLevel = GetLiquidState(block);
        Vector belowPos = position + Vector.Down;

        // Sanity check that the block update still matches the block in the world
        if (await world.GetBlockAsync(position) is IBlock b && !b.Is(block.Material))
        {
            return false;
        }

        var horizontalNeighbors = new Dictionary<Vector, IBlock?>() {
            {position + Vector.Forwards, await world.GetBlockAsync(position + Vector.Forwards)},
            {position + Vector.Backwards, await world.GetBlockAsync(position + Vector.Backwards)},
            {position + Vector.Left, await world.GetBlockAsync(position + Vector.Left)},
            {position + Vector.Right, await world.GetBlockAsync(position + Vector.Right)}
        };

        // Handle fluid interactions
        foreach (var (loc, neighborBlock) in horizontalNeighbors)
        {
            if (neighborBlock is null) { continue; }
            if (block.Is(BlocksRegistry.Water.Material) && neighborBlock.Is(BlocksRegistry.Lava.Material))
            {
                await world.SetBlockAsync(loc, GetLiquidState(neighborBlock) == 0 ? BlocksRegistry.ObsidianBlock : BlocksRegistry.Cobblestone);
            }
        }

        // Spread
        // Handle the initial search for closet path downwards.
        // Just going to do a crappy pathfind for now. We can do
        // proper pathfinding some other time.
        if (liquidLevel == 0)
        {
            var validPaths = new List<Vector>();
            var paths = horizontalNeighbors.Keys.ToList();

            foreach (var pathLoc in paths)
            {
                if (await world.GetBlockAsync(pathLoc) is IBlock pathSide &&
                    (TagsRegistry.Block.ReplaceableByLiquid.Entries.Contains(pathSide.RegistryId) || pathSide.IsLiquid))
                {
                    var pathBelow = await world.GetBlockAsync(pathLoc + Vector.Down);
                    if (pathBelow is IBlock &&
                        (TagsRegistry.Block.ReplaceableByLiquid.Entries.Contains(pathBelow.RegistryId) || pathBelow.IsLiquid))
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
            if (await world.GetBlockAsync(position + Vector.Up) is IBlock up && !up.IsLiquid)
            {
                await world.SetBlockAsync(position, BlocksRegistry.Air);
                await world.ScheduleBlockUpdateAsync(new BlockUpdate(world, belowPos));
                return false;
            }

            // Keep falling
            if (await world.GetBlockAsync(belowPos) is IBlock below && TagsRegistry.Block.ReplaceableByLiquid.Entries.Contains(below.RegistryId))
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
                await world.SetBlockUntrackedAsync(position, BlocksRegistry.Get(block.Material, DetermineBlockState(block.Material, liquidLevel)));
            }
        }

        if (liquidLevel < 8)
        {
            // Check infinite source blocks
            if (liquidLevel == 1 && block.Is(BlocksRegistry.Water.Material))
            {
                // if 2 neighbors are source blocks (state = 0), then become source block
                int sourceNeighborCount = 0;
                foreach (var (_, neighborBlock) in horizontalNeighbors)
                {
                    if (neighborBlock is IBlock && neighborBlock.Is(block.Material) && (GetLiquidState(neighborBlock) == 0))
                    {
                        sourceNeighborCount++;
                    }
                }

                if (sourceNeighborCount > 1)
                {
                    await world.SetBlockAsync(position, BlocksRegistry.Get(block.Material));
                    return true;
                }
            }

            // Check liquid discipate
            if (liquidLevel > 0)
            {
                // On some side of the block, there should be another water block with a lower state.
                int lowestState = liquidLevel;
                foreach (var (_, neighborBlock) in horizontalNeighbors)
                {
                    var neighborState = neighborBlock!.IsLiquid ? GetLiquidState(neighborBlock) : 0;

                    if (neighborBlock.Material == block.Material)
                        lowestState = Math.Min(lowestState, neighborState);
                }

                // If not, turn to air and update neighbors.
                var bUp = await world.GetBlockAsync(position + Vector.Up);
                if (lowestState >= liquidLevel && bUp!.Material != block.Material)
                {
                    await world.SetBlockAsync(position, BlocksRegistry.Air);
                    return true;
                }
            }

            // Handle falling downward
            if (await world.GetBlockAsync(belowPos) is IBlock below)
            {
                if (below.Material == block.Material) { return false; }

                // Handle lava landing on water
                if (block.Is(BlocksRegistry.Lava.Material) && below.Is(BlocksRegistry.Water.Material))
                {
                    await world.SetBlockAsync(position + Vector.Down, BlocksRegistry.Stone);
                    var update = new BlockUpdate(world, position, block);
                    await world.ScheduleBlockUpdateAsync(update);
                    return false;
                }

                // Handle water landing on lava
                if (block.Is(BlocksRegistry.Water.Material) && below.Is(BlocksRegistry.Lava.Material))
                {
                    await world.SetBlockAsync(position + Vector.Down, BlocksRegistry.ObsidianBlock);
                    var update = new BlockUpdate(world, position, block);
                    await world.ScheduleBlockUpdateAsync(update);
                    return false;
                }

                if (TagsRegistry.Block.ReplaceableByLiquid.Entries.Contains(below.RegistryId))
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

            // Spread horizontally
            foreach (var (loc, neighborBlock) in horizontalNeighbors)
            {
                if (neighborBlock is null) { continue; }

                var neighborState = neighborBlock.State is WaterState neighborWater ? neighborWater.Level : 0;

                if (TagsRegistry.Block.ReplaceableByLiquid.Entries.Contains(neighborBlock.RegistryId) ||
                    (neighborBlock.IsLiquid && neighborState > liquidLevel + 1))
                {
                    var newBlock = BlocksRegistry.Get(block.Material, DetermineBlockState(block.Material, liquidLevel + 1));
                    await world.SetBlockAsync(loc, newBlock);
                    var neighborUpdate = new BlockUpdate(world, loc, newBlock);
                    await world.ScheduleBlockUpdateAsync(neighborUpdate);

                    // If any of the neighbors of this new block are a different liquid,
                    // they need a block update
                    var newNeighbors = new Dictionary<Vector, IBlock?>() {
                        {loc + Vector.Forwards, await world.GetBlockAsync(loc + Vector.Forwards)},
                        {loc + Vector.Backwards, await world.GetBlockAsync(loc + Vector.Backwards)},
                        {loc + Vector.Left, await world.GetBlockAsync(loc + Vector.Left)},
                        {loc + Vector.Right, await world.GetBlockAsync(loc + Vector.Right)}
                    };
                    foreach (var (newloc, newNeighbor) in newNeighbors)
                    {
                        if (newNeighbor is null || !newNeighbor.IsLiquid) { continue; }
                        if (newNeighbor.Material != block.Material)
                        {
                            var newNeighborUpdate = new BlockUpdate(world, newloc, newNeighbor);
                            await world.ScheduleBlockUpdateAsync(newNeighborUpdate);
                        }
                    }
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
