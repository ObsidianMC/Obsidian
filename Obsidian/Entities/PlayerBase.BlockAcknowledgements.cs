using Obsidian.Net.Packets.Play.Clientbound;
using System.Threading;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    private readonly Lock blockAcknowledgementLock = new();
    private int pendingBlockSequence = -1;

    // The world ticks begun when the last handler that queued the acknowledgement finished: it waits for a world tick
    // begun after that to finish, which has sent that handler's block changes.
    private long blockAcknowledgementTicks = -1;

    // The handlers running that may queue an acknowledgement: none is sent before their block changes are made.
    private int blockAcknowledgementHolds;

    /// <summary>
    /// Records the highest received block prediction sequence. Vanilla acknowledges that maximum once per tick.
    /// Completion may happen after a later click, so it sends corrections independently and never queues an ack.
    /// </summary>
    internal void QueueBlockAcknowledgement(int sequence)
    {
        lock (this.blockAcknowledgementLock)
        {
            this.pendingBlockSequence = Math.Max(this.pendingBlockSequence, sequence);
            this.blockAcknowledgementTicks = this.WorldTicksStarted();
        }
    }

    /// <summary>
    /// Holds acknowledgements back while a handler that may queue one runs, until
    /// <see cref="EndBlockAcknowledgementHold"/>.
    /// </summary>
    internal void BeginBlockAcknowledgementHold()
    {
        lock (this.blockAcknowledgementLock)
            this.blockAcknowledgementHolds++;
    }

    /// <summary>
    /// Ends a handler's hold, and holds the queued acknowledgement back until the handler's block changes have been
    /// sent: block changes go out with the level's tick (see <see cref="AbstractLevel.FlushBlockChanges"/>), and a
    /// client given the acknowledgement first drops its prediction and shows the old block until they come.
    /// </summary>
    internal void EndBlockAcknowledgementHold()
    {
        lock (this.blockAcknowledgementLock)
        {
            this.blockAcknowledgementHolds--;
            if (this.pendingBlockSequence >= 0)
                this.blockAcknowledgementTicks = this.WorldTicksStarted();
        }
    }

    /// <summary>
    /// Sends the queued acknowledgement, if any, once no handler holds it back. With
    /// <paramref name="waitForWorldTick"/>, only once a world tick that began after its handlers finished has finished
    /// too; without (the server is paused and has sent the block changes itself, or a caller that has), at once.
    /// </summary>
    internal void FlushBlockAcknowledgement(bool waitForWorldTick = false)
    {
        lock (this.blockAcknowledgementLock)
        {
            if (this.pendingBlockSequence < 0 || this.blockAcknowledgementHolds > 0)
                return;

            if (waitForWorldTick && this.Server is Server server
                && server.WorldTicksCompleted <= this.blockAcknowledgementTicks)
            {
                return;
            }

            this.SendPacket(new BlockChangedAckPacket { SequenceID = this.pendingBlockSequence });
            this.pendingBlockSequence = -1;
        }
    }

    private long WorldTicksStarted() => this.Server is Server server ? server.WorldTicksStarted : 0;
}
