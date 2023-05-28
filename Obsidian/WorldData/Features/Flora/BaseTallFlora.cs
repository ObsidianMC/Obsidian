using Obsidian.Registries;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public abstract class BaseTallFlora : BaseFlora
{
    protected readonly IBlock blockWithLowerState;
    protected readonly IBlock blockWithUpperState;

    protected BaseTallFlora(GenHelper helper, Chunk chunk, Material floraMat, int maxHeight = 2, IBlockState? lowerState = null, IBlockState? upperState = null) : base(helper, chunk, floraMat)
    {
        this.blockWithLowerState = BlocksRegistry.Get(floraMat, lowerState);
        this.blockWithUpperState = BlocksRegistry.Get(floraMat, upperState);
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
            await helper.SetBlockAsync(placeVector + (0, y, 0), this.blockWithLowerState, chunk);
        }

        // Top
        await helper.SetBlockAsync(placeVector + (0, growHeight - 1, 0), this.blockWithUpperState, chunk);
        return true;
    }
}
