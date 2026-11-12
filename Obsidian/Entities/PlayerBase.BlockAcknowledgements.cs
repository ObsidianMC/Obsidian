using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System.Threading;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    // The handler running in the current flow, whose queued sequence it acknowledges once it's done.
    private static readonly AsyncLocal<BlockAcknowledgementScope?> acknowledgementScope = new();

    private readonly Lock blockAcknowledgementLock = new();

    // The highest sequence queued outside any handler's scope (by code calling a handler's work directly), sent by
    // FlushBlockAcknowledgement.
    private BlockAcknowledgement? unscopedAcknowledgement;

    // The last acknowledgement admitted to the client's output; it only goes up within a level epoch.
    private BlockAcknowledgement lastAcknowledgement = new(-1, -1);

    /// <summary>
    /// The connection's level epoch (see <see cref="IConditionalPacketSender.LevelEpoch"/>), or null for a connection
    /// that doesn't count level changes.
    /// </summary>
    internal LevelEpoch? LevelEpoch => (this.Connection as IConditionalPacketSender)?.LevelEpoch;

    /// <summary>
    /// Records a received block prediction sequence. Vanilla acknowledges the highest one once its block changes are
    /// sent: within a handler (see <see cref="BeginBlockAcknowledgementScope"/>), the handler acknowledges it when
    /// done. Completion may happen after a later click, so it sends corrections independently and never queues an ack.
    /// </summary>
    internal void QueueBlockAcknowledgement(int sequence)
    {
        var acknowledgement = new BlockAcknowledgement(sequence, this.LevelEpoch?.Number ?? 0);
        if (acknowledgementScope.Value is BlockAcknowledgementScope scope && scope.TryAdd(this, acknowledgement))
            return;

        lock (this.blockAcknowledgementLock)
            this.unscopedAcknowledgement = acknowledgement.Max(this.unscopedAcknowledgement);
    }

    /// <summary>
    /// Starts a packet handler that may queue an acknowledgement, for the rest of the current flow. Disposing the scope
    /// when the handler is done hands the highest sequence it queued to the player's level, which sends it after the
    /// next flush of block changes, which carries the handler's changes, and after that tick's block events (see
    /// <see cref="AbstractLevel.AcknowledgeAfterBlockChanges(PlayerBase, BlockAcknowledgement)"/>): a client given the
    /// acknowledgement first would drop its prediction and show the old block until the change came.
    /// </summary>
    internal BlockAcknowledgementScope BeginBlockAcknowledgementScope()
    {
        var scope = new BlockAcknowledgementScope(this);
        acknowledgementScope.Value = scope;
        return scope;
    }

    /// <summary>
    /// Sends the highest sequence queued outside any handler's scope, if any: for callers that run a handler's work
    /// directly and have sent its block changes themselves.
    /// </summary>
    internal void FlushBlockAcknowledgement()
    {
        BlockAcknowledgement? acknowledgement;
        lock (this.blockAcknowledgementLock)
        {
            acknowledgement = this.unscopedAcknowledgement;
            this.unscopedAcknowledgement = null;
        }

        if (acknowledgement is BlockAcknowledgement due)
            this.SendBlockAcknowledgement(due);
    }

    /// <summary>
    /// Sends <paramref name="acknowledgement"/>, unless it's stale where it falls in the client's output:
    /// <list type="bullet">
    /// <item>
    /// A level change was admitted since its sequence was received. The client's predictions live in its level, and
    /// it numbers them afresh in the next one: an old level's sequence would end the new level's predictions.
    /// </item>
    /// <item>
    /// An equal or higher sequence of the same level went out before it. The client ends every prediction up to the
    /// sequence it's given, so a lower one after it (one finished in a level whose flush came later) is out of order.
    /// </item>
    /// </list>
    /// </summary>
    internal void SendBlockAcknowledgement(BlockAcknowledgement acknowledgement)
    {
        var packet = new BlockChangedAckPacket { SequenceID = acknowledgement.Sequence };

        // Checked and admitted together, so two levels sending this player's acknowledgements keep them in order.
        if (this.Connection is IConditionalPacketSender connection)
        {
            connection.SendPacketIf(
                new SharedPacketBodies(packet), acknowledgement.LevelEpoch, () => this.TryAdvance(acknowledgement));
        }
        else if (this.TryAdvance(acknowledgement))
        {
            this.SendPacket(packet);
        }
    }

    // Records the acknowledgement as the last one sent, if it's newer than that one.
    private bool TryAdvance(BlockAcknowledgement acknowledgement)
    {
        lock (this.blockAcknowledgementLock)
        {
            if (acknowledgement.LevelEpoch == this.lastAcknowledgement.LevelEpoch
                && acknowledgement.Sequence <= this.lastAcknowledgement.Sequence)
            {
                return false;
            }

            this.lastAcknowledgement = acknowledgement;
            return true;
        }
    }

    // The handler's changes went to the player's level as it is when the handler is done. A player in no level gets
    // the acknowledgement at once: there's no flush to wait for.
    private void AcknowledgeAfterBlockChanges(BlockAcknowledgement acknowledgement)
    {
        if (this.Level is AbstractLevel level)
            level.AcknowledgeAfterBlockChanges(this, acknowledgement);
        else
            this.SendBlockAcknowledgement(acknowledgement);
    }

    /// <summary>
    /// A packet handler's acknowledgement: the highest sequence it queued, handed on when it's disposed (see
    /// <see cref="BeginBlockAcknowledgementScope"/>).
    /// </summary>
    internal sealed class BlockAcknowledgementScope(PlayerBase player) : IDisposable
    {
        private readonly Lock gate = new();
        private BlockAcknowledgement? acknowledgement;
        private bool ended;

        // Queued after the handler ended (by work it left running), or for another player, it isn't this scope's.
        public bool TryAdd(PlayerBase queuing, BlockAcknowledgement queued)
        {
            if (!ReferenceEquals(queuing, player))
                return false;

            lock (this.gate)
            {
                if (this.ended)
                    return false;

                this.acknowledgement = queued.Max(this.acknowledgement);
                return true;
            }
        }

        public void Dispose()
        {
            BlockAcknowledgement? queued;
            lock (this.gate)
            {
                if (this.ended)
                    return;

                this.ended = true;
                queued = this.acknowledgement;
            }

            if (queued is BlockAcknowledgement due)
                player.AcknowledgeAfterBlockChanges(due);
        }
    }
}

/// <summary>
/// A block prediction sequence to acknowledge, with the level epoch of the connection when it was received (see
/// <see cref="IConditionalPacketSender.LevelEpoch"/>): the client numbers its predictions afresh in each level.
/// </summary>
internal readonly record struct BlockAcknowledgement(int Sequence, int LevelEpoch)
{
    /// <summary>The one to send of the two: the later level's, or else the higher sequence.</summary>
    public BlockAcknowledgement Max(BlockAcknowledgement? other) => other is not BlockAcknowledgement known
        ? this
        : this.LevelEpoch != known.LevelEpoch
            ? (this.LevelEpoch > known.LevelEpoch ? this : known)
            : (this.Sequence >= known.Sequence ? this : known);
}
