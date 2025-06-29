using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public abstract class BaseFlora(GenHelper helper, IChunk chunk, Material mat = Material.RedTulip)
{
    protected IBlock FloraBlock { get; } = BlocksRegistry.Get(mat);
    protected GenHelper GenHelper { get; } = helper;

    protected IChunk Chunk { get; } = chunk;

    protected Material FloraMat { get; set; } = mat;

    protected int Height { get; set; } = 1;

    protected List<Material> GrowsIn { get; set; } =
    [
        Material.Air
    ];

    protected List<Material> GrowsOn { get; set; } =
    [
        Material.GrassBlock,
        Material.Dirt,
        Material.Podzol
    ];

    /// <summary>
    /// Place a grouping of plants in a circular patch.
    /// </summary>
    /// <param name="origin">Center of the grouping.</param>
    /// <param name="seed">World Seed.</param>
    /// <param name="radius">Radius of circular patch.</param>
    /// <param name="density">less dense: 1 < density < 10 :more dense.</param>
    public virtual async Task GenerateFloraAsync(Vector origin, int seed, int radius, int density)
    {
        density = Math.Max(1, 10 - density);
        var seedRand = new XorshiftRandom(seed + origin.GetHashCode());

        for (int rz = 0; rz <= radius * 2; rz++)
        {
            for (int rx = 0; rx <= radius * 2; rx++)
            {
                if ((radius - rx) * (radius - rx) + (radius - rz) * (radius - rz) <= (radius * radius))
                {
                    int x = origin.X - radius + rx;
                    int z = origin.Z - radius + rz;
                    int y = await GenHelper.GetWorldHeightAsync(x, z, Chunk) ?? -1;
                    if (y == -1) { continue; }
                    bool isFlora = seedRand.Next(10) % density == 0;
                    var placeVec = new Vector(x, y + 1, z);
                    if (isFlora)
                    {
                        await TryPlaceFloraAsync(placeVec);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Place a single plant.
    /// </summary>
    /// <param name="placeVector">The position above the surface block.</param>
    /// <returns>Whether plant was planted.</returns>
    public virtual async Task<bool> TryPlaceFloraAsync(Vector placeVector)
    {
        if (await GetGrowHeightAsync(placeVector) >= Height && await GetValidSurfaceAsync(placeVector))
        {
            await GenHelper.SetBlockAsync(placeVector, this.FloraBlock, Chunk);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Check if the surface is compatible.
    /// </summary>
    /// <param name="loc">The position above the surface block.</param>
    /// <returns>Whether surface is compatible.</returns>
    protected virtual async Task<bool> GetValidSurfaceAsync(Vector loc) => await GenHelper.GetBlockAsync(loc + Vector.Down, Chunk) is IBlock b && GrowsOn.Contains(b.Material);

    /// <summary>
    /// Check free space above grow location.
    /// </summary>
    /// <param name="loc">Location to sample.</param>
    /// <returns>Count of vertical free space above plant.</returns>
    protected virtual async Task<int> GetGrowHeightAsync(Vector loc)
    {
        int freeSpace = 0;
        for (int y = 0; y < Height; y++)
        {
            if (await GenHelper.GetBlockAsync(loc + (0, y, 0), Chunk) is IBlock above && GrowsIn.Contains(above.Material))
            {
                freeSpace++;
            }
        }
        return freeSpace;
    }
}
