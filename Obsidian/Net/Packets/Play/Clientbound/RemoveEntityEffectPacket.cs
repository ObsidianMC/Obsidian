using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class RemoveEntityEffectPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }
    
    [Field(1)]
    public byte EffectId { get; init; }
    
    public int Id => 0x3B;

    public RemoveEntityEffectPacket(int entityId, byte effectId)
    {
        EntityId = entityId;
        EffectId = effectId;
    }
}
