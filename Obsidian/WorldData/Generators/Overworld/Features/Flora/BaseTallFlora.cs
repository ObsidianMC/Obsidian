﻿namespace Obsidian.WorldData.Generators.Overworld.Features.Flora;

public abstract class BaseTallFlora : BaseFlora
{
    protected readonly short lowerState;
    protected readonly short upperState;

    protected BaseTallFlora(World world, Material floraMat, int maxHeight = 2, int lowerState = 1, int upperState = 0) : base(world, floraMat)
    {
        this.lowerState = (short)lowerState;
        this.upperState = (short)upperState;
        this.height = maxHeight;
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

        // Grow base
        for (int y = 0; y < growHeight - 1; y++)
        {
            await world.SetBlockUntrackedAsync(placeVector + (0, y, 0), new Block(FloraMat, lowerState));
        }

        // Top
        await world.SetBlockUntrackedAsync(placeVector + (0, growHeight - 1, 0), new Block(FloraMat, upperState));
        return true;
    }
}
