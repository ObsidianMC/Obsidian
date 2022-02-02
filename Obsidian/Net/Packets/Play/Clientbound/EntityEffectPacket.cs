using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class EntityEffectPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }
    
    [Field(1)]
    public byte EffectId { get; init; }
    
    [Field(2)]
    public byte Amplifier { get; init; }
    
    [Field(3), VarLength]
    public int Duration { get; init; }
    
    [Field(4)]
    public byte Flags { get; init; }
    
    public int Id => 0x65;

    public EntityEffectPacket(int entityId, byte effectId, int duration)
    {
        EntityId = entityId;
        EffectId = effectId;
        Duration = duration;
    }
}
