using Obsidian.Net.Packets.Play.Clientbound;
using System.Threading;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private readonly BlockEventQueue blockEvents = new();

    internal int PendingBlockEvents => this.blockEvents.Count;

    internal void QueueBlockEvent(Vector position, int block, int id, int parameter,
        Func<ValueTask<bool>>? trigger = null, Func<int>? parameterAtDispatch = null)
    {
        this.blockEvents.Enqueue(new(position, block, id, parameter), async () =>
        {
            var accepted = trigger is null
                ? block != BlockIds.NoteBlock || this.TriggerNoteBlockEvent(position)
                : await trigger();
            if (!accepted)
                return;

            // Block updates use immediate broadcasts too: a later piston write must not overtake this event.
            this.PacketBroadcaster.BroadcastToLevelInRange(this, (VectorD)position, new BlockEventPacket
            {
                Position = position,
                BlockType = block,
                ActionId = (byte)id,
                ActionParam = (byte)(parameterAtDispatch?.Invoke() ?? parameter)
            });
        });
    }

    internal ValueTask ProcessBlockEventsAsync() => this.blockEvents.DrainAsync(this.IsBlockTicking,
        position => this.GameEventBlock(position)?.RegistryId, this.ReconcileContainerLids);

    internal bool IsBlockTicking(Vector position)
    {
        var x = position.X >> 4;
        var z = position.Z >> 4;
        if (this.GetLoadedChunk(x, z) is null)
            return false;

        // Vanilla ChunkLevel: block events tick at level <= 32, one ring beyond entity ticking (<= 31).
        // Dilating the entity-ticking squares also includes the block-ticking border of ticking tickets.
        var snapshot = Volatile.Read(ref this.simulationCurrent);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                if (this.IsTicketTicking(x + dx, z + dz)
                    || snapshot && this.simulation.Contains(x + dx, z + dz))
                {
                    return true;
                }
            }
        }

        if (snapshot)
            return false;

        var distance = this.Configuration.SimulationDistance + 1;
        foreach (var (_, player) in this.Players)
        {
            if (player.GameMode == GameMode.Spectator)
                continue;

            var center = Region.ChunkOf(player.Position);
            if (Math.Abs(center.X - x) <= distance && Math.Abs(center.Z - z) <= distance)
                return true;
        }

        return false;
    }
}
