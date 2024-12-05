using Obsidian.Nbt;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BlockEntityDataPacket
{
    [Field(0)]
    public Vector Position { get; init; }

    [Field(1), VarLength]
    private EntityType Type { get; init; }

    [Field(2)]
    public INbtTag NBTData { get; init; } = default!;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WritePosition(Position);
        writer.WriteVarInt(Type);
        ((MinecraftStream)writer).WriteNbt(NBTData);
    }
}
