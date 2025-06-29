using Obsidian.WorldData.Generators.Overworld;
using System.Security.Cryptography;
using System.Text;

namespace Obsidian.WorldData.Generators;

public class GenHelper
{
    private readonly IWorld world;

    internal int Seed { get; private set; }

    internal OverworldTerrainNoise Noise { get; private set; }

    public GenHelper(IWorld world)
    {
        this.world = world;
        if (!int.TryParse(world.Seed, out int seedHash))
            seedHash = BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes(world.Seed)));
        Seed = seedHash;
        Noise = new OverworldTerrainNoise(Seed);
    }

    public async ValueTask SetBlockAsync(Vector position, IBlock block, IChunk? chunk)
    {
        if (chunk is IChunk c && position.X >> 4 == c.X && position.Z >> 4 == c.Z)
        {
            c.SetBlock(position, block);
        }
        else
        {
            await world.SetBlockUntrackedAsync(position, block, false);
        }
    }

    public ValueTask SetBlockAsync(int x, int y, int z, IBlock block, IChunk? chunk) => SetBlockAsync(new Vector(x, y, z), block, chunk);

    public ValueTask SetBlockAsync(int x, int y, int z, IBlock block) => world.SetBlockUntrackedAsync(x, y, z, block, false);

    public ValueTask SetBlockAsync(Vector position, IBlock block) => world.SetBlockUntrackedAsync(position, block, false);

    public async ValueTask<IBlock?> GetBlockAsync(Vector position, IChunk? chunk)
    {
        if (chunk is IChunk c && position.X >> 4 == c.X && position.Z >> 4 == c.Z)
        {
            return c.GetBlock(position);
        }
        return await world.GetBlockAsync(position);
    }

    public ValueTask<IBlock?> GetBlockAsync(int x, int y, int z, IChunk? chunk) => GetBlockAsync(new Vector(x, y, z), chunk);

    public ValueTask<IBlock?> GetBlockAsync(int x, int y, int z) => world.GetBlockAsync(x, y, z);

    public ValueTask<IBlock?> GetBlockAsync(Vector position) => world.GetBlockAsync(position);

    public async ValueTask<int?> GetWorldHeightAsync(int x, int z, IChunk? chunk)
    {
        if (chunk is Chunk c && x >> 4 == c.X && z >> 4 == c.Z)
        {
            return c.Heightmaps[HeightmapType.MotionBlocking].GetHeight(NumericsHelper.Modulo(x, 16), NumericsHelper.Modulo(z, 16));
        }
        return await world.GetWorldSurfaceHeightAsync(x, z);
    }
}
