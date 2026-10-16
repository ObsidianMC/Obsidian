using Obsidian.Nbt;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// A block entity's update data (vanilla's <c>getUpdateTag</c>), which clients load into the block entity of that
/// type they have at the position.
/// </summary>
public partial class BlockEntityDataPacket
{
    [Field(0)]
    public Vector Position { get; init; }

    /// <summary>
    /// The block entity type's network id.
    /// </summary>
    [Field(1), VarLength]
    public int Type { get; init; }

    [Field(2)]
    public NbtCompound NBTData { get; init; } = default!;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WritePosition(Position);
        writer.WriteVarInt(Type);
        writer.WriteNbtCompound(this.NBTData);
    }
}
