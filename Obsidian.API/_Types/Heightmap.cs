using Obsidian.API.Utilities;
using System.Numerics;
using System.Threading;

namespace Obsidian.API;
public sealed class Heightmap
{
    public HeightmapType HeightmapType { get; }

    internal DataArray data;

    private readonly IChunk chunk;

    private readonly Lock dataLock = new();

    // Bound by the host's block registry, which owns the captured block physics tables.
    internal static Func<HeightmapType, IBlock, bool> Matches { get; set; } =
        (_, _) => throw new InvalidOperationException("The block registry has not initialized heightmap predicates.");

    public Predicate<IBlock> Predicate;

    private readonly Predicate<IBlock> defaultPredicate;

    // 1 while the heights changed since the chunk's last save took the mark (see TakeUnsaved). A new one is unsaved.
    private int unsaved = 1;

    /// <summary>
    /// Whether the heights changed since <see cref="TakeUnsaved"/> last cleared the mark.
    /// </summary>
    internal bool IsUnsaved => Volatile.Read(ref this.unsaved) != 0;

    /// <summary>
    /// Clears the mark, for a save about to write the heights.
    /// </summary>
    /// <returns>Whether it was marked. A write after this marks it again, so the next save has it.</returns>
    internal bool TakeUnsaved() => Interlocked.Exchange(ref this.unsaved, 0) != 0;

    public Heightmap(HeightmapType type, IChunk chunk)
    {
        HeightmapType = type;
        this.chunk = chunk;
        // Like vanilla, entries hold 0 to the chunk height inclusive: ceil(log2(height + 1)) bits.
        data = new DataArray(32 - BitOperations.LeadingZeroCount((uint)chunk.Height), 256);

        this.defaultPredicate = this.DefaultMatches;
        Predicate = this.defaultPredicate;
    }

    private Heightmap(HeightmapType type, IChunk chunk, DataArray data)
    {
        HeightmapType = type;
        this.chunk = chunk;
        this.data = data;

        this.defaultPredicate = this.DefaultMatches;
        Predicate = this.defaultPredicate;
    }

    private bool DefaultMatches(IBlock block) => Matches(this.HeightmapType, block);

    public bool Update(int x, int y, int z, IBlock blockState) =>
        this.Update(x, y, z, blockState, 0, 0, default);

    /// <summary>Uses the host's captured state masks for the default predicate, preserving custom predicates.</summary>
    internal bool Update(int x, int y, int z, IBlock blockState, int mask, int bit, ReadOnlySpan<byte> stateMasks)
    {
        lock (this.dataLock)
        {
            if (!ReferenceEquals(this.Predicate, this.defaultPredicate))
                bit = 0;

            return this.UpdateCore(x, y, z, blockState, mask, bit, stateMasks);
        }
    }

    private bool UpdateCore(int x, int y, int z, IBlock blockState, int mask, int bit, ReadOnlySpan<byte> stateMasks)
    {
        var index = GetIndex(x, z);
        var height = this.GetHeight(index);
        if (y <= height - 2)
            return false;

        var matches = bit == 0 ? this.Predicate(blockState) : (mask & bit) != 0;
        if (matches)
        {
            if (y >= height)
            {
                this.SetCore(index, y + 1);
                return true;
            }
        }
        else if (height - 1 == y)
        {
            for (int i = y - 1; i >= this.chunk.MinY; --i)
            {
                var otherBlock = this.chunk.GetBlock(x, i, z);

                if (this.MatchesState(otherBlock, bit, stateMasks))
                {
                    this.SetCore(index, i + 1);

                    return true;
                }
            }

            this.SetCore(index, this.chunk.MinY);

            return true;
        }

        return false;
    }

    private bool MatchesState(IBlock block, int bit, ReadOnlySpan<byte> stateMasks)
    {
        if (bit == 0)
            return this.Predicate(block);

        var stateId = block.GetHashCode();
        return (uint)stateId < (uint)stateMasks.Length
            ? (stateMasks[stateId] & bit) != 0
            : this.Predicate(block);
    }

    public void Set(int x, int z, int value)
    {
        lock (this.dataLock)
        {
            this.SetCore(GetIndex(x, z), value);
        }
    }

    private void SetCore(int index, int value)
    {
        this.data[index] = value - this.chunk.MinY;
        Volatile.Write(ref this.unsaved, 1);
    }

    public int GetHeight(int x, int z)
    {
        lock (this.dataLock)
        {
            return this.GetHeight(GetIndex(x, z));
        }
    }

    private int GetHeight(int value) => this.data[value] + this.chunk.MinY;

    private static int GetIndex(int x, int z) => (x & 15) + (z & 15) * 16;

    /// <summary>Rebuilds an absent saved heightmap, or explicitly refreshes one from its chunk's blocks.</summary>
    internal void Recalculate()
    {
        lock (this.dataLock)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var y = this.chunk.MinY + this.chunk.Height - 1;
                    while (y >= this.chunk.MinY && !this.Predicate(this.chunk.GetBlock(x, y, z)))
                        y--;

                    this.SetCore(GetIndex(x, z), y + 1);
                }
            }
        }
    }

    /// <summary>
    /// A copy of the packed heights, as chunk packets send them. Change heights with <see cref="Set"/>.
    /// </summary>
    public long[] GetDataArray()
    {
        lock (this.dataLock)
        {
            return (long[])this.data.storage.Clone();
        }
    }

    public Heightmap Clone() => Clone(chunk);

    public Heightmap Clone(IChunk chunk)
    {
        lock (this.dataLock)
        {
            var clone = new Heightmap(HeightmapType, chunk, data.Clone());
            if (!ReferenceEquals(this.Predicate, this.defaultPredicate))
                clone.Predicate = this.Predicate;

            return clone;
        }
    }
}

