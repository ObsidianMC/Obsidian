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
    private int unscopedBlockSequence = -1;

    /// <summary>
    /// Records a received block prediction sequence. Vanilla acknowledges the highest one once its block changes are
    /// sent: within a handler (see <see cref="BeginBlockAcknowledgementScope"/>), the handler acknowledges it when
    /// done. Completion may happen after a later click, so it sends corrections independently and never queues an ack.
    /// </summary>
    internal void QueueBlockAcknowledgement(int sequence)
    {
        if (acknowledgementScope.Value is BlockAcknowledgementScope scope && scope.TryAdd(this, sequence))
            return;

        lock (this.blockAcknowledgementLock)
            this.unscopedBlockSequence = Math.Max(this.unscopedBlockSequence, sequence);
    }

    /// <summary>
    /// Starts a packet handler that may queue an acknowledgement, for the rest of the current flow. Disposing the scope
    /// when the handler is done hands the highest sequence it queued to the player's level, whose next flush of block
    /// changes sends the handler's changes and then the acknowledgement (see
    /// <see cref="AbstractLevel.AcknowledgeAfterBlockChanges"/>): a client given the acknowledgement first would drop
    /// its prediction and show the old block until the change came.
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
        int sequence;
        lock (this.blockAcknowledgementLock)
        {
            sequence = this.unscopedBlockSequence;
            this.unscopedBlockSequence = -1;
        }

        if (sequence >= 0)
            this.SendPacket(new BlockChangedAckPacket { SequenceID = sequence });
    }

    // The handler's changes went to the player's level as it is when the handler is done. A player in no level gets
    // the acknowledgement at once: there's no flush to wait for.
    private void AcknowledgeAfterBlockChanges(int sequence)
    {
        if (this.Level is AbstractLevel level)
            level.AcknowledgeAfterBlockChanges(this, sequence);
        else
            this.SendPacket(new BlockChangedAckPacket { SequenceID = sequence });
    }

    /// <summary>
    /// A packet handler's acknowledgement: the highest sequence it queued, handed on when it's disposed (see
    /// <see cref="BeginBlockAcknowledgementScope"/>).
    /// </summary>
    internal sealed class BlockAcknowledgementScope(PlayerBase player) : IDisposable
    {
        private readonly Lock gate = new();
        private int sequence = -1;
        private bool ended;

        // Queued after the handler ended (by work it left running), or for another player, it isn't this scope's.
        public bool TryAdd(PlayerBase queuing, int queued)
        {
            if (!ReferenceEquals(queuing, player))
                return false;

            lock (this.gate)
            {
                if (this.ended)
                    return false;

                this.sequence = Math.Max(this.sequence, queued);
                return true;
            }
        }

        public void Dispose()
        {
            int queued;
            lock (this.gate)
            {
                if (this.ended)
                    return;

                this.ended = true;
                queued = this.sequence;
            }

            if (queued >= 0)
                player.AcknowledgeAfterBlockChanges(queued);
        }
    }
}
