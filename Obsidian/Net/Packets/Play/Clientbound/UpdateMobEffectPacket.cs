using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateMobEffectPacket(int entityId, int effectId, int duration)
{
    [Field(0), VarLength]
    public int EntityId { get; init; } = entityId;

    [Field(1), VarLength]
    public int EffectId { get; init; } = effectId;

    [Field(2), VarLength]
    public int Amplifier { get; init; }

    [Field(3), VarLength]
    public int Duration { get; init; } = duration;

    [Field(4), ActualType(typeof(sbyte))]
    public EntityEffect Flags { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.EntityId);
        writer.WriteVarInt(this.EffectId);
        writer.WriteVarInt(this.Amplifier);
        writer.WriteVarInt(this.Duration);
        writer.WriteByte(this.Flags);
    }
}

