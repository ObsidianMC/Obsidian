using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// Where the client moved the vehicle it controls (vanilla's <c>ServerboundMoveVehiclePacket</c>). The server takes
/// it for plugins' mobs and for boats, after checking it on the vehicle's tick (see
/// <see cref="ClientVehicleControl"/>), and ignores it for vanilla's mounts, which it moves from the rider's input.
/// </summary>
public partial class MoveVehiclePacket
{
    public VectorD Position { get; private set; }

    public float Yaw { get; private set; }

    public float Pitch { get; private set; }

    public bool OnGround { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Position = VectorD.Read(reader);
        this.Yaw = reader.ReadSingle();
        this.Pitch = reader.ReadSingle();
        this.OnGround = reader.ReadBoolean();
    }

    public override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        var move = new VehicleMove(this.Position, this.Yaw, this.Pitch, this.OnGround);
        if (player.Vehicle is PluginMob vehicle)
            vehicle.ClientControl.Enqueue(player, move);
        else if (player.Vehicle is Boat boat)
            boat.ClientControl.Enqueue(player, move);

        return default;
    }
}
