namespace Obsidian.WorldData.Generators.Overworld.Features.Flora;

public abstract class BaseTallFlora : BaseFlora
{
    protected readonly ushort lowerState;
    protected readonly ushort upperState;

    protected BaseTallFlora(GenHelper helper, Chunk chunk, Material floraMat, int maxHeight = 2, int lowerState = 1, int upperState = 0) : base(helper, chunk, floraMat)
    {
        this.lowerState = (ushort)lowerState;
        this.upperState = (ushort)upperState;
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
            await helper.SetBlockAsync(placeVector + (0, y, 0), this.floraBlock, chunk);//TODO LOWER STATE
        }

        // Top
        await helper.SetBlockAsync(placeVector + (0, growHeight - 1, 0), this.floraBlock, chunk);//TODO UPPER STATE
        return true;
    }
}
