using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class EntityVelocity : IClientboundPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1)]
    public Velocity Velocity { get; init; }

    public int Id => 0x4F;
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();
}
