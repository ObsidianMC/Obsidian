﻿using Obsidian.API.BlockStates.Builders;
using Obsidian.Registries;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class BambooFlora : BaseTallFlora
{
    public BambooFlora(GenHelper helper, Chunk chunk) : 
        base(helper, chunk, Material.Bamboo, 15)
    {

    }

    /// <summary>
    /// Place a single plant.
    /// </summary>
    /// <param name="placeVector">The position above the surface block.</param>
    /// <returns>Whether plant was planted.</returns>
    public override async Task<bool> TryPlaceFloraAsync(Vector placeVector)
    {
        if (!await GetValidSurfaceAsync(placeVector)) { return false; }
        int growHeight = await GetGrowHeightAsync(placeVector);
        if (growHeight == 0) { return false; }

        var bambooBase = BlocksRegistry.Get(this.FloraMat, new BambooStateBuilder().WithAge(1).WithLeaves(LeavesType.None).WithStage(1).Build());
        var bambooLeaves = BlocksRegistry.Get(this.FloraMat, new BambooStateBuilder().WithAge(1).WithLeaves(LeavesType.Small).WithStage(1).Build());
        var bambooLeavesFull = BlocksRegistry.Get(this.FloraMat, new BambooStateBuilder().WithAge(1).WithLeaves(LeavesType.Large).WithStage(1).Build());

        // Grow base
        for (int y = 0; y < growHeight - 3; y++)
        {
            await helper.SetBlockAsync(placeVector + (0, y, 0), bambooBase, chunk);
        }
        await helper.SetBlockAsync(placeVector + (0, growHeight - 3, 0), bambooLeaves, chunk);
        await helper.SetBlockAsync(placeVector + (0, growHeight - 2, 0), bambooLeaves, chunk);
        await helper.SetBlockAsync(placeVector + (0, growHeight - 1, 0), bambooLeavesFull, chunk);
        await helper.SetBlockAsync(placeVector + (0, growHeight, 0), bambooLeavesFull, chunk);
        return true;
    }
}
