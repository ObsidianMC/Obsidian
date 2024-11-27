using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BlockDestructionPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1)]
    public Vector Position { get; init; }

    /// <summary>
    /// 0-9 to set it, any other value to remove it.
    /// </summary>
    [Field(2)]
    public sbyte DestroyStage { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(EntityId);
        writer.WritePosition(Position);
        writer.WriteByte(DestroyStage);
    }
}
