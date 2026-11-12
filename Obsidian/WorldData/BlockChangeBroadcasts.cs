using Obsidian.Net.Packets.Play.Clientbound;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// Collects the blocks a level changed for its players, and sends them once a tick per chunk section, like vanilla's
/// <c>ChunkHolder</c>: a block update for a section with one changed block, or a section update listing the current
/// state of each changed block.
/// </summary>
/// <remarks>
/// Changes are recorded from any thread (the level's tick, packet handlers, plugins). <see cref="Flush"/> runs at the
/// level tick's chunk broadcast and, while the server is paused, on its connection pass; changes made anywhere else
/// wait for the next one. A flush reads each block's state as it is then, so a block changed several times in a tick
/// is sent once, in its final state.
/// </remarks>
internal sealed class BlockChangeBroadcasts(AbstractLevel level)
{
    // Taken only to record a change or swap the pending ones out; nothing is called under it.
    private readonly Lock pendingLock = new();

    // Held for a whole flush, so two flushes (a tick's and a paused connection pass's) never interleave their packets.
    private readonly Lock flushLock = new();

    // The chunks with changes, in the order they first changed since the last flush.
    private OrderedDictionary<long, ChangedChunk> pending = [];

    /// <summary>Records that the block at <paramref name="position"/> changed, for the next flush.</summary>
    public void Changed(Vector position)
    {
        if (level.IsOutsideBuildHeight(position.Y))
            return;

        var chunkX = position.X >> 4;
        var chunkZ = position.Z >> 4;
        var key = NumericsHelper.IntsToLong(chunkX, chunkZ);
        var sectionIndex = (position.Y - level.MinY) >> 4;
        var positionInSection = SectionBlocksUpdatePacket.PackPosition(position.X, position.Y, position.Z);

        lock (this.pendingLock)
        {
            if (!this.pending.TryGetValue(key, out var chunk))
            {
                chunk = new ChangedChunk(chunkX, chunkZ, level.Height >> 4);
                this.pending.Add(key, chunk);
            }

            chunk.Add(sectionIndex, positionInSection);
        }
    }

    /// <summary>
    /// Sends the changes recorded since the last flush to the players who have their chunks: per chunk, its sections
    /// from the bottom up. Changes in chunks that are no longer loaded are dropped, as vanilla drops them.
    /// </summary>
    public void Flush()
    {
        lock (this.flushLock)
        {
            OrderedDictionary<long, ChangedChunk> changed;
            lock (this.pendingLock)
            {
                if (this.pending.Count == 0)
                    return;

                changed = this.pending;
                this.pending = [];
            }

            foreach (var (key, chunk) in changed)
            {
                var players = PlayersWithChunk(key);
                if (players.Count == 0)
                    continue;

                foreach (var packet in this.ReadPackets(chunk))
                    level.PacketBroadcaster.BroadcastTo(packet, players);
            }
        }
    }

    // The players that have the chunk, read once for all its sections.
    private List<IPlayer> PlayersWithChunk(long key)
    {
        var players = new List<IPlayer>();
        foreach (var (_, player) in level.Players)
        {
            if (player.LoadedChunks.Contains(key))
                players.Add(player);
        }

        return players;
    }

    // The chunk's packets, with each block's state read now under the tick lock, which every block write holds.
    private List<IClientboundPacket> ReadPackets(ChangedChunk chunk)
    {
        var packets = new List<IClientboundPacket>();
        lock (level.Fluids.TickLock)
        {
            if (level.GetLoadedChunk(chunk.X, chunk.Z) is null)
                return packets;

            for (var sectionIndex = 0; sectionIndex < chunk.Sections.Length; sectionIndex++)
            {
                if (chunk.Sections[sectionIndex] is not SectionChanges section)
                    continue;

                var sectionY = (level.MinY >> 4) + sectionIndex;
                packets.Add(this.ReadPacket(chunk, sectionY, section));
            }
        }

        return packets;
    }

    private IClientboundPacket ReadPacket(ChangedChunk chunk, int sectionY, SectionChanges section)
    {
        var positions = section.ToArray();
        if (positions.Length == 1)
        {
            var position = Position(chunk, sectionY, positions[0]);
            return new BlockUpdatePacket(position, level.BlockUpdates.GetBlock(position).GetHashCode());
        }

        var changes = new long[positions.Length];
        for (var index = 0; index < positions.Length; index++)
        {
            var state = level.BlockUpdates.GetBlock(Position(chunk, sectionY, positions[index])).GetHashCode();
            changes[index] = SectionBlocksUpdatePacket.PackChange(state, positions[index]);
        }

        return new SectionBlocksUpdatePacket(chunk.X, sectionY, chunk.Z, changes);
    }

    private static Vector Position(ChangedChunk chunk, int sectionY, short positionInSection) => new(
        chunk.X << 4 | positionInSection >> 8 & 15,
        sectionY << 4 | positionInSection & 15,
        chunk.Z << 4 | positionInSection >> 4 & 15);

    private sealed class ChangedChunk(int x, int z, int sectionCount)
    {
        public int X { get; } = x;

        public int Z { get; } = z;

        /// <summary>The changed positions of each section, by its index from the bottom of the level.</summary>
        public SectionChanges?[] Sections { get; } = new SectionChanges?[sectionCount];

        public void Add(int sectionIndex, short positionInSection)
        {
            var section = this.Sections[sectionIndex] ??= new SectionChanges();
            section.Add(positionInSection);
        }
    }
}

/// <summary>
/// The changed positions of one chunk section, listed in the order vanilla's <c>ChunkHolder</c> lists them: the order
/// of fastutil's <c>ShortOpenHashSet</c>, which a section update's entries follow. Clients don't depend on it, but it
/// keeps the packets byte for byte the same as vanilla's.
/// </summary>
/// <remarks>
/// It's the same open-addressing table as fastutil's: 16 expected entries at a load factor of 0.75, linear probing
/// from fastutil's <c>HashCommon.mix</c> of the key, zero kept apart, and the same growth and rehash. Listing runs
/// from zero (if present) and then from the table's end to its start.
/// </remarks>
internal sealed class SectionChanges
{
    private const float LoadFactor = 0.75f;
    private const int InitialExpected = 16;

    // fastutil's HashCommon.INT_PHI.
    private const int Phi = unchecked((int)0x9E3779B9);

    // Zero marks a free slot, so a changed position of zero is kept apart, as fastutil keeps it.
    private short[] keys;
    private int mask;
    private int maxFill;
    private bool containsZero;

    public SectionChanges()
    {
        var size = ArraySize(InitialExpected);
        this.keys = new short[size];
        this.mask = size - 1;
        this.maxFill = MaxFill(size);
    }

    public int Count { get; private set; }

    public void Add(short position)
    {
        if (position == 0)
        {
            if (this.containsZero)
                return;

            this.containsZero = true;
        }
        else
        {
            var slot = Mix(position) & this.mask;
            while (this.keys[slot] != 0)
            {
                if (this.keys[slot] == position)
                    return;

                slot = (slot + 1) & this.mask;
            }

            this.keys[slot] = position;
        }

        // Like fastutil, the table grows once the count before this entry reached its fill limit.
        if (this.Count++ >= this.maxFill)
            this.Rehash(ArraySize(this.Count + 1));
    }

    /// <summary>The positions, in vanilla's order.</summary>
    public short[] ToArray()
    {
        var positions = new short[this.Count];
        var index = 0;
        if (this.containsZero)
            positions[index++] = 0;

        for (var slot = this.keys.Length - 1; slot >= 0; slot--)
        {
            if (this.keys[slot] != 0)
                positions[index++] = this.keys[slot];
        }

        return positions;
    }

    // fastutil's rehash: the old table's keys from its end to its start, each probed into the new table.
    private void Rehash(int size)
    {
        var newMask = size - 1;
        var newKeys = new short[size];
        for (var slot = this.keys.Length - 1; slot >= 0; slot--)
        {
            var key = this.keys[slot];
            if (key == 0)
                continue;

            var target = Mix(key) & newMask;
            while (newKeys[target] != 0)
                target = (target + 1) & newMask;

            newKeys[target] = key;
        }

        this.keys = newKeys;
        this.mask = newMask;
        this.maxFill = MaxFill(size);
    }

    private static int Mix(int key)
    {
        var hash = unchecked(key * Phi);
        return hash ^ (int)((uint)hash >> 16);
    }

    // fastutil's HashCommon.arraySize: the power of two at or above expected / load factor.
    private static int ArraySize(int expected) =>
        (int)Math.Max(2, System.Numerics.BitOperations.RoundUpToPowerOf2((uint)MathF.Ceiling(expected / LoadFactor)));

    // fastutil's HashCommon.maxFill.
    private static int MaxFill(int size) => Math.Min((int)MathF.Ceiling(size * LoadFactor), size - 1);
}
