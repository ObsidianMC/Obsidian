using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class DestroyEntities : IClientboundPacket
{
    [Field(0), VarLength]
    public List<int> Entities { get; private set; } = new();

    public int Id => 0x3A;
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();

    public DestroyEntities(params int[] entities)
    {
        Entities = entities.ToList();
    }
}
