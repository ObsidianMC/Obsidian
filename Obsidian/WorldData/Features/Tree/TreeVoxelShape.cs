using System.Collections;

namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Filled cells of a box, like vanilla's <c>BitSetDiscreteVoxelShape</c> as used by tree placement.
/// </summary>
internal sealed class TreeVoxelShape
{
    private readonly BitArray storage;

    public TreeVoxelShape(int sizeX, int sizeY, int sizeZ)
    {
        this.SizeX = sizeX;
        this.SizeY = sizeY;
        this.SizeZ = sizeZ;
        this.storage = new BitArray(sizeX * sizeY * sizeZ);
    }

    public int SizeX { get; }

    public int SizeY { get; }

    public int SizeZ { get; }

    public bool IsFull(int x, int y, int z) => this.storage[this.Index(x, y, z)];

    public void Fill(int x, int y, int z) => this.storage[this.Index(x, y, z)] = true;

    /// <summary>
    /// Calls <paramref name="consumer"/> for every face between a filled cell and an empty one (or the box edge), with
    /// the direction pointing out of the filled cell, in vanilla's <c>DiscreteVoxelShape.forAllFaces</c> order:
    /// north/south faces, then down/up, then west/east.
    /// </summary>
    public void ForAllFaces(Action<BlockFace, int, int, int> consumer)
    {
        // Z faces: x outer, y middle, z inner.
        for (var x = 0; x < this.SizeX; x++)
        {
            for (var y = 0; y < this.SizeY; y++)
                this.ScanRun(consumer, BlockFace.North, BlockFace.South, x, y, 0, 0, 0, 1, this.SizeZ);
        }

        // Y faces: z outer, x middle, y inner.
        for (var z = 0; z < this.SizeZ; z++)
        {
            for (var x = 0; x < this.SizeX; x++)
                this.ScanRun(consumer, BlockFace.Down, BlockFace.Up, x, 0, z, 0, 1, 0, this.SizeY);
        }

        // X faces: y outer, z middle, x inner.
        for (var y = 0; y < this.SizeY; y++)
        {
            for (var z = 0; z < this.SizeZ; z++)
                this.ScanRun(consumer, BlockFace.West, BlockFace.East, 0, y, z, 1, 0, 0, this.SizeX);
        }
    }

    /// <summary>
    /// Reports where runs of filled cells start (negative face) and end (positive face) along the line from (x, y, z) in
    /// steps of (dx, dy, dz).
    /// </summary>
    private void ScanRun(Action<BlockFace, int, int, int> consumer, BlockFace negativeFace, BlockFace positiveFace,
        int x, int y, int z, int dx, int dy, int dz, int size)
    {
        var previous = false;
        for (var i = 0; i <= size; i++)
        {
            var current = i != size && this.IsFull(x + i * dx, y + i * dy, z + i * dz);
            if (!previous && current)
                consumer(negativeFace, x + i * dx, y + i * dy, z + i * dz);

            if (previous && !current)
                consumer(positiveFace, x + (i - 1) * dx, y + (i - 1) * dy, z + (i - 1) * dz);

            previous = current;
        }
    }

    private int Index(int x, int y, int z) => (x * this.SizeY + y) * this.SizeZ + z;
}
