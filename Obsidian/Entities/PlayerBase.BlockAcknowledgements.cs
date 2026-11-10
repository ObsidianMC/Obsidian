using Obsidian.Net.Packets.Play.Clientbound;
using System.Threading;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    private readonly Lock blockAcknowledgementLock = new();
    private int pendingBlockSequence = -1;
    private int acknowledgedBlockSequence = -1;

    /// <summary>
    /// Records the highest received block prediction sequence. Vanilla acknowledges that maximum once per tick.
    /// Completion may happen after a later click, so it sends corrections independently and never queues an ack.
    /// </summary>
    internal void QueueBlockAcknowledgement(int sequence)
    {
        lock (this.blockAcknowledgementLock)
            this.pendingBlockSequence = Math.Max(this.pendingBlockSequence, sequence);
    }

    internal void FlushBlockAcknowledgement()
    {
        lock (this.blockAcknowledgementLock)
        {
            if (this.pendingBlockSequence > this.acknowledgedBlockSequence)
            {
                this.SendPacket(new BlockChangedAckPacket { SequenceID = this.pendingBlockSequence });
                this.acknowledgedBlockSequence = this.pendingBlockSequence;
            }

            this.pendingBlockSequence = -1;
        }
    }
}
