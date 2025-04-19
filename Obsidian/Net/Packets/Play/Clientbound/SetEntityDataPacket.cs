using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetEntityDataPacket
{
    [Field(0), VarLength]
    public required int EntityId { get; init; }

    [Field(1)]
    public required IEntity Entity { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.EntityId);
        writer.WriteEntity(this.Entity);
    }
}
