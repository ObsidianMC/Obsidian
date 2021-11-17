using Obsidian.Utilities.Collection;
using Obsidian.WorldData;

namespace Obsidian.ChunkData;

//TODO make better impl of heightmaps
public class Heightmap
{
    public const string MOTION_BLOCKING = "MOTION_BLOCKING";
    public HeightmapType HeightmapType { get; set; }

    internal DataArray data = new DataArray(9, 256);

    private Chunk chunk;

    public Predicate<Block> Predicate;

    public Heightmap(HeightmapType type, Chunk chunk)
    {
        this.HeightmapType = type;
        this.chunk = chunk;

        if (type == HeightmapType.MotionBlocking)
            this.Predicate = (block) => !block.IsAir || !block.IsFluid;
    }

    public bool Update(int x, int y, int z, Block blockState)
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

            for (int i = y - 1; i >= 0; --i)
            {
                pos = new Vector(x, i, z);
                var otherBlock = this.chunk.GetBlock(pos);

                if (this.Predicate(otherBlock))
                {
                    this.Set(x, z, i + 1);

                    return true;
                }
            }

            this.Set(x, z, 0);

            return true;
        }

        return false;
    }

    public void Set(int x, int z, int value) => this.data[this.GetIndex(x, z)] = value;

    public int GetHeight(int x, int z) => this.GetHeight(this.GetIndex(x, z));

    private int GetHeight(int value) => this.data[value];

    private int GetIndex(int x, int z) => x + z * 16;

    public long[] GetDataArray() => this.data.Storage;
}

public enum HeightmapType
{
    MotionBlocking,
    MotionBlockingNoLeaves,

    OceanFloor,
    OceanFloorWG,

    WorldSurface,
    WorldSurfaceWG
}
