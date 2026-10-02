using Obsidian.API.Utilities;
using System.Numerics;

namespace Obsidian.API;
public sealed class Heightmap
{
    public HeightmapType HeightmapType { get; }

    internal DataArray data;

    private readonly IChunk chunk;

    public Predicate<IBlock> Predicate;

    public Heightmap(HeightmapType type, IChunk chunk)
    {
        HeightmapType = type;
        this.chunk = chunk;
        // Like vanilla, entries hold 0 to the chunk height inclusive: ceil(log2(height + 1)) bits.
        data = new DataArray(32 - BitOperations.LeadingZeroCount((uint)chunk.Height), 256);

        Predicate = type == HeightmapType.MotionBlocking ? ((block) => !block.IsAir || !block.IsLiquid) : (_ => false);
    }

    private Heightmap(HeightmapType type, IChunk chunk, DataArray data)
    {
        HeightmapType = type;
        this.chunk = chunk;
        this.data = data;

        Predicate = type == HeightmapType.MotionBlocking ? ((block) => !block.IsAir || !block.IsLiquid) : (_ => false);
    }

    public bool Update(int x, int y, int z, IBlock blockState)
    {
        int height = this.GetHeight(x, z);

        if (y <= height - 2)
            return false;

        if (this.Predicate(blockState))
        {
            if (y >= height)
            {
                this.Set(x, z, y + 1);
                return true;
            }
        }
        else if (height - 1 == y)
        {
            Vector pos;

            for (int i = y - 1; i >= this.chunk.MinY; --i)
            {
                pos = new Vector(x, i, z);
                var otherBlock = this.chunk.GetBlock(pos);

                if (this.Predicate(otherBlock))
                {
                    this.Set(x, z, i + 1);

                    return true;
                }
            }

            this.Set(x, z, this.chunk.MinY);

            return true;
        }

        return false;
    }

    public void Set(int x, int z, int value) => this.data[GetIndex(x, z)] = value - this.chunk.MinY;

    public int GetHeight(int x, int z) => this.GetHeight(GetIndex(x, z));

    private int GetHeight(int value) => this.data[value] + this.chunk.MinY;

    private static int GetIndex(int x, int z) => x + z * 16;

    public long[] GetDataArray() => this.data.storage;

    public Heightmap Clone() => Clone(chunk);

    public Heightmap Clone(IChunk chunk) => new Heightmap(HeightmapType, chunk, data.Clone());
}

