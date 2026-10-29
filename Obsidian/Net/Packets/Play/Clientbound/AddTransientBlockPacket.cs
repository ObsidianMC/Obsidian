namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>A client-side transient block, without changing the world's stored blocks.</summary>
public partial class AddTransientBlockPacket(Vector position, int blockState)
{
    public Vector Position { get; } = position;
    public int BlockState { get; } = blockState;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WritePosition(this.Position);
        writer.WriteVarInt(this.BlockState);
    }
}
