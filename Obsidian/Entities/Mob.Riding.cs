using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class Mob
{
    protected Player? Rider { get; private set; }

    protected bool Mount(Player player)
    {
        if (Rider != null || player.Vehicle != null || player.Level != Level || !Alive ||
            !player.Alive || player.GameMode == GameMode.Spectator)
            return false;
        Rider = player;
        player.Vehicle = this;
        SynchronizePassengers();
        return true;
    }

    internal void Dismount()
    {
        if (Rider == null)
            return;
        Rider.Vehicle = null;
        Rider = null;
        SynchronizePassengers();
    }

    private void SynchronizePassengers() => PacketBroadcaster.QueuePacketToLevelInRange(Level, Position,
        new SetPassengersPacket { EntityId = EntityId, Passengers = Rider == null ? [] : [Rider.EntityId] }, EntityId);

    protected virtual void TickRidden()
    {
        if (Rider != null && (Rider.Level != Level || !Rider.Alive || Rider.Sneaking || !Alive))
            Dismount();
    }

    private void UpdateRider()
    {
        if (Rider == null)
            return;
        var position = Position + new VectorD(0, Dimension.Height, 0);
        if (Level is Obsidian.WorldData.AbstractLevel level && !level.TryMoveEntity(Rider, Rider.Position, position))
        {
            Dismount();
            return;
        }
        Rider.LastPosition = Rider.Position;
        Rider.Position = position;
        Rider.HeadY = position.Y + 1.62;
    }
}
