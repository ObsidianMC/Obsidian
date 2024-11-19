using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class RemoveEntitiesPacket
{
    [Field(0), VarLength]
    public List<int> Entities { get; private set; } = new();

    public RemoveEntitiesPacket(params int[] entities)
    {
        this.Entities = entities.ToList();
    }
}
