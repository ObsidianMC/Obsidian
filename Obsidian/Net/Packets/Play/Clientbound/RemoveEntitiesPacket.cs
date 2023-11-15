using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class RemoveEntitiesPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public List<int> Entities { get; private set; } = [];

    public int Id => 0x3E;

    public RemoveEntitiesPacket(params int[] entities)
    {
        this.Entities = entities.ToList();
    }
}
