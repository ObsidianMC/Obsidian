using Obsidian.Nbt;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BlockEntityDataPacket
{
    [Field(0)]
    public Vector Position { get; init; }

    [Field(1), VarLength]
    private int Type { get; init; }

    [Field(2)]
    public NbtCompound NBTData { get; init; } = default!;

    /// <summary>Creates the update for the block entity of the given registry type id, e.g. <c>minecraft:campfire</c> = 33.</summary>
    public static BlockEntityDataPacket Create(Vector position, int blockEntityType, NbtCompound nbt) =>
        new() { Position = position, Type = blockEntityType, NBTData = nbt };

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WritePosition(Position);
        writer.WriteVarInt(Type);
        writer.WriteNbtCompound(this.NBTData);
    }
}
