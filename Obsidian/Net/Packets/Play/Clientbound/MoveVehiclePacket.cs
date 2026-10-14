namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Puts the vehicle a client moves back where the server has it, after the server refused a move (vanilla's
/// <c>ClientboundMoveVehiclePacket</c>).
/// </summary>
public partial class MoveVehiclePacket
{
    public required VectorD Position { get; init; }

    public required float Yaw { get; init; }

    public required float Pitch { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteDouble(this.Position.X);
        writer.WriteDouble(this.Position.Y);
        writer.WriteDouble(this.Position.Z);
        writer.WriteSingle(this.Yaw);
        writer.WriteSingle(this.Pitch);
    }
}
