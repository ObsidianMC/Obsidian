using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class RemoveEntitiesPacket(params int[] entities)
{
    [Field(0), VarLength]
    public List<int> Entities { get; private set; } = entities.ToList();

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Entities.Count);

        foreach(var entityId in this.Entities)
            writer.WriteVarInt(entityId);
    }
}
