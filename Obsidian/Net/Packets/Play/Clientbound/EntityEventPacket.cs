using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class EntityEventPacket
{
    [Field(0)]
    public required int EntityId { get; init; }
    [Field(1)]
    public required byte Event { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteInt(EntityId);
        writer.WriteByte(Event);
    }
}
