using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class Mob
{
    private readonly List<Player> passengers = [];
    protected Player? Rider => passengers.FirstOrDefault();
    protected IReadOnlyList<Player> Passengers => passengers;
    protected virtual int MaximumPassengers => 1;

    protected bool Mount(Player player)
    {
        if (passengers.Count >= MaximumPassengers || player.Vehicle != null || player.Level != Level || !Alive ||
            !player.Alive || player.GameMode == GameMode.Spectator)
            return false;
        passengers.Add(player);
        player.Vehicle = this;
        SynchronizePassengers();
        return true;
    }

    internal void Dismount()
    {
        if (passengers.Count == 0) return;
        foreach (var passenger in passengers) passenger.Vehicle = null;
        passengers.Clear();
        SynchronizePassengers();
    }

    internal void Dismount(Player player)
    {
        if (!passengers.Remove(player)) return;
        player.Vehicle = null;
        SynchronizePassengers();
    }

    private void SynchronizePassengers() => PacketBroadcaster.QueuePacketToLevelInRange(Level, Position,
        new SetPassengersPacket { EntityId = EntityId, Passengers = passengers.Select(player => player.EntityId).ToArray() }, EntityId);

    protected virtual void TickRidden()
    {
        foreach (var passenger in passengers.ToArray())
            if (passenger.Level != Level || !passenger.Alive || passenger.Sneaking || !Alive) Dismount(passenger);
    }

    protected virtual VectorD PassengerOffset(int index) => new(0, Dimension.Height, 0);

    private void UpdateRider()
    {
        for (var index = 0; index < passengers.Count; index++)
        {
            var passenger = passengers[index];
            var position = Position + PassengerOffset(index);
            if (Level is Obsidian.WorldData.AbstractLevel level && !level.TryMoveEntity(passenger, passenger.Position, position))
            { Dismount(passenger); index--; continue; }
            passenger.LastPosition = passenger.Position;
            passenger.Position = position;
            passenger.HeadY = position.Y + 1.62;
        }
    }
}
