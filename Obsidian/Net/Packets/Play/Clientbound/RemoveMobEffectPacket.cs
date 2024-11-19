using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class RemoveMobEffectPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1), VarLength]
    public int EffectId { get; init; }

    public RemoveMobEffectPacket(int entityId, int effectId)
    {
        EntityId = entityId;
        EffectId = effectId;
    }
}
