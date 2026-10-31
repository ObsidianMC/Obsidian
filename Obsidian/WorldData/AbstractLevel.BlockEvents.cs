using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private readonly BlockEventQueue blockEvents = new();

    internal int PendingBlockEvents => this.blockEvents.Count;

    internal void QueueBlockEvent(Vector position, int block, int id, int parameter,
        Func<ValueTask<bool>>? trigger = null)
    {
        this.blockEvents.Enqueue(new(position, block, id, parameter), async () =>
        {
            var accepted = trigger is null
                ? block != BlockIds.NoteBlock || this.TriggerNoteBlockEvent(position)
                : await trigger();
            if (!accepted)
                return;

            this.PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)position, new BlockEventPacket
            {
                Position = position,
                BlockType = block,
                ActionId = (byte)id,
                ActionParam = (byte)parameter
            });
        });
    }

    internal ValueTask ProcessBlockEventsAsync() => this.blockEvents.DrainAsync(
        position => this.IsMobTicking((VectorD)position),
        position => this.GameEventBlock(position)?.RegistryId);
}
