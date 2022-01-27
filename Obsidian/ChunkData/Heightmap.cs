﻿using Obsidian.Utilities.Collection;
using Obsidian.WorldData;

namespace Obsidian.ChunkData;

//TODO make better impl of heightmaps
public sealed class Heightmap
{
    public HeightmapType HeightmapType { get; }

    internal DataArray data;

    private Chunk chunk;

    public Predicate<Block> Predicate;

    public Heightmap(HeightmapType type, Chunk chunk)
    {
        HeightmapType = type;
        this.chunk = chunk;
        data = new DataArray(9, 256);

        if (type == HeightmapType.MotionBlocking)
            Predicate = (block) => !block.IsAir || !block.IsFluid;
        else
            Predicate = _ => false;
    }

    private Heightmap(HeightmapType type, Chunk chunk, DataArray data)
    {
        HeightmapType = type;
        this.chunk = chunk;
        this.data = data;

        if (type == HeightmapType.MotionBlocking)
            Predicate = (block) => !block.IsAir || !block.IsFluid;
        else
            Predicate = _ => false;
    }

    public bool Update(int x, int y, int z, Block blockState)
    {
        int height = GetHeight(x, z);

        if (y <= height - 2)
            return false;

        if (Predicate(blockState))
        {
            if (y >= height)
            {
                Set(x, z, y + 1);
                return true;
            }
        }
        else if (height - 1 == y)
        {
            Vector pos;

            for (int i = y - 1; i >= 0; --i)
            {
                pos = new Vector(x, i, z);
                var otherBlock = chunk.GetBlock(pos);

                if (Predicate(otherBlock))
                {
                    Set(x, z, i + 1);

                    return true;
                }
            }

            Set(x, z, 0);

            return true;
        }

        return false;
    }

    public void Set(int x, int z, int value) => data[GetIndex(x, z)] = value - -64;

    public int GetHeight(int x, int z) => GetHeight(GetIndex(x, z));

    private int GetHeight(int value) => data[value] + -64;

    private int GetIndex(int x, int z) => x + z * 16;

    public long[] GetDataArray() => data.storage;

    public Heightmap Clone()
    {
        return Clone(chunk);
    }

    public Heightmap Clone(Chunk chunk)
    {
        return new Heightmap(HeightmapType, chunk, data.Clone());
    }
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
