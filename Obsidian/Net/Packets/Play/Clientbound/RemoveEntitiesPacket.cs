using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class RemoveEntitiesPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public List<int> Entities { get; private set; } = new();

    public int Id => 0x42;

    public RemoveEntitiesPacket(params int[] entities)
    {
        this.Entities = entities.ToList();
    }
}
