namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>
/// A chunk's post-processing marks in the order <see cref="BiomeTerrain.PostProcess"/> walks them, in buffers the
/// thread reuses for its next chunk.
/// </summary>
internal readonly struct PostProcessMarks
{
    // A thread's buffers, taken while a chunk's marks are in use.
    [ThreadStatic]
    private static Vector[]? freePositions;

    [ThreadStatic]
    private static long[]? freeKeys;

    private readonly Vector[] positions;
    private readonly long[] keys;
    private readonly int count;

    private PostProcessMarks(Vector[] positions, long[] keys, int count)
    {
        this.positions = positions;
        this.keys = keys;
        this.count = count;
    }

    public ReadOnlySpan<Vector> Positions => this.positions.AsSpan(0, this.count);

    /// <summary>
    /// Orders marks by their section, keeping the order they were added within a section, as vanilla walks them.
    /// </summary>
    public static PostProcessMarks SortBySection(List<Vector> marks, int minY)
    {
        var count = marks.Count;
        var positions = Take(ref freePositions, count);
        var keys = Take(ref freeKeys, count);

        // Each key is unique, the section above the mark's index, so an unstable sort keeps the added order.
        for (var i = 0; i < count; i++)
        {
            var position = marks[i];
            positions[i] = position;
            keys[i] = ((long)((position.Y - minY) >> 4) << 32) | (uint)i;
        }

        keys.AsSpan(0, count).Sort(positions.AsSpan(0, count));
        return new PostProcessMarks(positions, keys, count);
    }

    /// <summary>Hands the buffers back to the thread for its next chunk.</summary>
    public void Return()
    {
        freePositions = this.positions;
        freeKeys = this.keys;
    }

    private static T[] Take<T>(ref T[]? free, int count)
    {
        var buffer = free;
        free = null;

        if (buffer is null || buffer.Length < count)
            buffer = new T[Math.Max(count, 256)];

        return buffer;
    }
}
