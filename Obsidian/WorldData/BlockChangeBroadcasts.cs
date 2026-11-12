using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// Collects the blocks a level changed for its players, and sends them once a tick per chunk section, like vanilla's
/// <c>ChunkHolder</c>: a block update for a section with one changed block, or a section update listing the current
/// state of each changed block. Prediction acknowledgements go out behind them, after the tick's block events (see
/// <see cref="Acknowledge"/>).
/// </summary>
/// <remarks>
/// <para>
/// Changes are recorded from any thread (the level's tick, packet handlers, plugins). <see cref="Flush"/> runs at the
/// level tick's chunk broadcast and, while the server is paused, on its connection pass; changes made anywhere else
/// wait for the next one. A flush reads each block's state as it is then, so a block changed several times in a tick
/// is sent once, in its final state.
/// </para>
/// <para>
/// Acknowledgements still waiting when the level is disposed are dropped. Its players have left it by then, and a
/// client that left a level dropped its predictions with it; one that hasn't is covered by its next acknowledgement,
/// which ends every prediction up to a higher sequence.
/// </para>
/// </remarks>
internal sealed class BlockChangeBroadcasts(AbstractLevel level)
{
    // Guards everything below; nothing is sent or called under it.
    private readonly Lock pendingLock = new();

    // The chunks with changes, in the order they first changed since the last flush took them.
    private OrderedDictionary<long, ChangedChunk> pending = [];

    // Each player's acknowledgement from finished handlers, waiting for a flush to send their changes.
    private Dictionary<PlayerBase, BlockAcknowledgement> acknowledgements = [];

    // Acknowledgements whose changes a flush has sent, waiting for SendAcknowledgements.
    private Dictionary<PlayerBase, BlockAcknowledgement> flushed = [];

    // Whether a flush is under way, which owns sending: two flushes never interleave their packets. What's recorded
    // meanwhile stays pending for the next flush.
    private bool flushing;

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
    /// Acknowledges a finished handler's sequence to <paramref name="player"/> once the next flush has sent its block
    /// changes, at the next <see cref="SendAcknowledgements"/>. Vanilla acknowledges from its connection tick, after
    /// the levels' ticks, so a client sees a tick's block changes, then its block events, then the acknowledgement.
    /// Until the acknowledgement, the client keeps the server's corrections in its prediction cache, and runs block
    /// events against the block it predicted.
    /// </summary>
    /// <remarks>
    /// An acknowledgement for a player who has left the level by then goes out only if the client hasn't been sent the
    /// level change yet (see <see cref="PlayerBase.SendBlockAcknowledgement"/>).
    /// </remarks>
    public void Acknowledge(PlayerBase player, BlockAcknowledgement acknowledgement)
    {
        lock (this.pendingLock)
            Record(this.acknowledgements, player, acknowledgement);
    }

    /// <summary>
    /// Sends the changes recorded since the last flush to the players who have their chunks, per chunk its sections
    /// from the bottom up. Changes in chunks that are no longer loaded are dropped, as vanilla drops them. The
    /// acknowledgements recorded before it are sent at the next <see cref="SendAcknowledgements"/>.
    /// </summary>
    /// <remarks>
    /// A flush sends what was recorded when it began, once: what's recorded while it sends waits for the next flush,
    /// so a writer that never stops can't hold the level's tick here. The next flush always comes, with the next tick
    /// or, while the server is paused, the next connection pass. If another flush is under way, this one returns at
    /// once, and what it would have sent is either taken by that one or still pending when that one ends: no flush
    /// waits for another's sends, which a stuck connection could hold.
    /// </remarks>
    public void Flush()
    {
        OrderedDictionary<long, ChangedChunk>? changed;
        Dictionary<PlayerBase, BlockAcknowledgement>? acknowledged;
        lock (this.pendingLock)
        {
            if (this.flushing)
                return;

            this.flushing = true;
            changed = this.pending.Count > 0 ? this.pending : null;
            acknowledged = this.acknowledgements.Count > 0 ? this.acknowledgements : null;
            if (changed is not null)
                this.pending = [];

            if (acknowledged is not null)
                this.acknowledgements = [];
        }

        var sent = false;
        try
        {
            if (changed is not null)
                this.Send(changed);

            sent = true;
        }
        finally
        {
            // Ownership ends under the lock that records changes, with the acknowledgements whose changes went out.
            // A send that failed part way drops them: a later, higher acknowledgement covers them.
            lock (this.pendingLock)
            {
                if (sent && acknowledged is not null)
                {
                    foreach (var (player, acknowledgement) in acknowledged)
                        Record(this.flushed, player, acknowledgement);
                }

                this.flushing = false;
            }
        }
    }

    /// <summary>
    /// Sends the acknowledgements whose block changes a flush has sent. The level's tick calls it after its block
    /// events, as vanilla's connection tick follows the levels' ticks, and after a last flush if it fails.
    /// </summary>
    public void SendAcknowledgements()
    {
        Dictionary<PlayerBase, BlockAcknowledgement> due;
        lock (this.pendingLock)
        {
            if (this.flushed.Count == 0)
                return;

            due = this.flushed;
            this.flushed = [];
        }

        foreach (var (player, acknowledgement) in due)
            player.SendBlockAcknowledgement(acknowledgement);
    }

    private static void Record(
        Dictionary<PlayerBase, BlockAcknowledgement> into, PlayerBase player, BlockAcknowledgement acknowledgement) =>
        into[player] = into.TryGetValue(player, out var known) ? acknowledgement.Max(known) : acknowledgement;

    private void Send(OrderedDictionary<long, ChangedChunk> changed)
    {
        foreach (var (key, chunk) in changed)
        {
            var players = this.PlayersWithChunk(key);
            if (players.Count == 0)
                continue;

            foreach (var packet in this.ReadPackets(chunk))
            {
                var shared = new SharedPacketBodies(packet);
                foreach (var recipient in players)
                    Send(recipient, shared);
            }
        }
    }

    // Block packets name no level: one a player received after leaving for another level would change that level's
    // block there. Each is sent only if no level change was admitted to the player's output since they were chosen,
    // and they're still in this level with the chunk, checked as the packet is admitted (see IConditionalPacketSender).
    // The level alone wouldn't do: a player who left and came back is in it again, but a packet admitted before their
    // return's respawn would reach the client in the level they went to.
    private static void Send(Recipient recipient, SharedPacketBodies shared)
    {
        if (recipient.Player is PlayerBase { Connection: IConditionalPacketSender connection })
            connection.SendPacketIf(shared, recipient.LevelEpoch, recipient.Wanted);
        else if (recipient.Wanted())
            recipient.Player.SendPacket(shared.Packet);
    }

    // The players that have the chunk, read once for all its sections, each with its level epoch and the check made
    // again as it's sent.
    private List<Recipient> PlayersWithChunk(long key)
    {
        var players = new List<Recipient>();
        foreach (var (_, player) in level.Players)
        {
            // The epoch first: a level change sets the player's level before it admits its respawn. A player whose
            // client hasn't been sent their move to this level yet isn't a recipient: their level and chunks may
            // already be this level's, but the client is still in the level they're leaving.
            var epoch = (player as PlayerBase)?.LevelEpoch;
            if (epoch is not null && !ReferenceEquals(epoch.Level, level))
                continue;

            bool Wanted() => ReferenceEquals(player.Level, level) && player.LoadedChunks.Contains(key);
            if (Wanted())
                players.Add(new Recipient(player, epoch?.Number ?? 0, Wanted));
        }

        return players;
    }

    private readonly record struct Recipient(IPlayer Player, int LevelEpoch, Func<bool> Wanted);

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
