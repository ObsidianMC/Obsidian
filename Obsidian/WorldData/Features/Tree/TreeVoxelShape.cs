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
                this.ScanRun(this.SizeZ, z => this.IsFull(x, y, z), z => consumer(BlockFace.North, x, y, z), z => consumer(BlockFace.South, x, y, z));
        }

        // Y faces: z outer, x middle, y inner.
        for (var z = 0; z < this.SizeZ; z++)
        {
            for (var x = 0; x < this.SizeX; x++)
                this.ScanRun(this.SizeY, y => this.IsFull(x, y, z), y => consumer(BlockFace.Down, x, y, z), y => consumer(BlockFace.Up, x, y, z));
        }

        // X faces: y outer, z middle, x inner.
        for (var y = 0; y < this.SizeY; y++)
        {
            for (var z = 0; z < this.SizeZ; z++)
                this.ScanRun(this.SizeX, x => this.IsFull(x, y, z), x => consumer(BlockFace.West, x, y, z), x => consumer(BlockFace.East, x, y, z));
        }
    }

    /// <summary>Reports where runs of filled cells start (negative face) and end (positive face) along one line.</summary>
    private void ScanRun(int size, Func<int, bool> isFull, Action<int> negativeFace, Action<int> positiveFace)
    {
        var previous = false;
        for (var i = 0; i <= size; i++)
        {
            var current = i != size && isFull(i);
            if (!previous && current)
                negativeFace(i);

            if (previous && !current)
                positiveFace(i - 1);

            previous = current;
        }
    }

    private int Index(int x, int y, int z) => (x * this.SizeY + y) * this.SizeZ + z;
}
