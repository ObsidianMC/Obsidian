using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// Which of its boat's paddles the client turns (vanilla's <c>ServerboundPaddleBoatPacket</c>), shown to other
/// players. It counts only for the boat the player controls.
/// </summary>
public partial class PaddleBoatPacket
{
    public bool Left { get; private set; }

    public bool Right { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Left = reader.ReadBoolean();
        this.Right = reader.ReadBoolean();
    }

    public override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (player.Vehicle is Boat boat && boat.Passengers.Count > 0 && ReferenceEquals(boat.Passengers[0], player))
            boat.QueuePaddles(this.Left, this.Right);

        return default;
    }
}
