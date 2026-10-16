using Obsidian.Nbt;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BlockEntityDataPacket
{
    [Field(0)]
    public Vector Position { get; init; }

    [Field(1), VarLength]
    private BlockEntityType Type { get; init; }

    [Field(2)]
    public NbtCompound NBTData { get; init; } = default!;

    /// <summary>Creates the update for a block entity of the given type, e.g. <see cref="BlockEntityType.Campfire"/>.</summary>
    public static BlockEntityDataPacket Create(Vector position, BlockEntityType blockEntityType, NbtCompound nbt) =>
        new() { Position = position, Type = blockEntityType, NBTData = nbt };

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WritePosition(Position);
        writer.WriteVarInt((int)Type);
        writer.WriteNbtCompound(this.NBTData);
    }
}
