using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class RemoveMobEffectPacket(int entityId, int effectId)
{
    [Field(0), VarLength]
    public int EntityId { get; init; } = entityId;

    [Field(1), VarLength]
    public int EffectId { get; init; } = effectId;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.EntityId);

        //This might've changed
        writer.WriteVarInt(this.EffectId);
    }
}
