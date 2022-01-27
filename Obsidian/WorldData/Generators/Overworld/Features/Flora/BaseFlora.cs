namespace Obsidian.WorldData.Generators.Overworld.Features.Flora;

public abstract class BaseFlora
{
    protected readonly World world;

    protected Material FloraMat { get; set; }

    protected int height = 1;

    protected List<Material> growsIn = new()
    {
        Material.Air
    };

    protected List<Material> growsOn = new()
    {
        Material.GrassBlock,
        Material.Dirt,
        Material.Podzol
    };

    protected BaseFlora(World world, Material mat = Material.RedTulip)
    {
        this.world = world;
        FloraMat = mat;
    }

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
        var seedRand = new Random(seed + origin.GetHashCode());

        for (int rz = 0; rz <= radius * 2; rz++)
        {
            for (int rx = 0; rx <= radius * 2; rx++)
            {
                if ((radius - rx) * (radius - rx) + (radius - rz) * (radius - rz) <= (radius * radius))
                {
                    int x = origin.X - radius + rx;
                    int z = origin.Z - radius + rz;
                    int y = await world.GetWorldSurfaceHeightAsync(x, z) ?? -1;
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
        if (await GetGrowHeightAsync(placeVector) >= height && await GetValidSurfaceAsync(placeVector))
        {
            await world.SetBlockUntrackedAsync(placeVector, new Block(FloraMat));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Check if the surface is compatible.
    /// </summary>
    /// <param name="loc">The position above the surface block.</param>
    /// <returns>Whether surface is compatible.</returns>
    protected virtual async Task<bool> GetValidSurfaceAsync(Vector loc) => await world.GetBlockAsync(loc + Vector.Down) is Block b && growsOn.Contains(b.Material);

    /// <summary>
    /// Check free space above grow location.
    /// </summary>
    /// <param name="loc">Location to sample.</param>
    /// <returns>Count of vertical free space above plant.</returns>
    protected virtual async Task<int> GetGrowHeightAsync(Vector loc)
    {
        int freeSpace = 0;
        for (int y = 0; y < height; y++)
        {
            if (await world.GetBlockAsync(loc + (0, y, 0)) is Block above && growsIn.Contains(above.Material))
            {
                freeSpace++;
            }
        }
        return freeSpace;
    }
}
