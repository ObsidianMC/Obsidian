namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Moves an entity by a small amount, in 1/4096 blocks on each axis, and turns it.
/// </summary>
public partial class MoveEntityPosRotPacket
{
    public int EntityId { get; init; }

    /// <summary>
    /// The move, in 1/4096 blocks, from the position last sent. Each axis must fit a short.
    /// </summary>
    public Vector Delta { get; init; }

    public Angle Yaw { get; init; }

    public Angle Pitch { get; init; }

    public bool OnGround { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.EntityId);
        EntityMoveDelta.Write(writer, this.Delta, this.OnGround);

        writer.WriteByte(this.Yaw.Value);
        writer.WriteByte(this.Pitch.Value);
    }
}
