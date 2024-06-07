using Obsidian.Nbt;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class EntityEffectPacket(int entityId, int effectId, int duration) : IClientboundPacket
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

    public int Id => 0x76;
}

[Flags]
public enum EntityEffect : sbyte
{
    IsAmbient = 0x01,
    ShowParticles = 0x02,
    ShowIcon = 0x04,
    Blend = 0x08
}
