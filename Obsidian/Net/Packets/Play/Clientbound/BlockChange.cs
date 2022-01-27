using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BlockChange : IClientboundPacket
{
    [Field(0)]
    public Vector Position { get; }

    [Field(1), VarLength]
    public int BlockId { get; }

    public int Id => 0x0C;
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();

    public BlockChange(Vector position, int block)
    {
        Position = position;
        BlockId = block;
    }
}
