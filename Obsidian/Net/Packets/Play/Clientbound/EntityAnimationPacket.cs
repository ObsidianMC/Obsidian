using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class EntityAnimationPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1), ActualType(typeof(byte))]
    public EntityAnimationType Animation { get; init; }

    public int Id => 0x06;
}

public enum EntityAnimationType : byte
{
    SwingMainArm,
    TakeDamage,
    LeaveBed,
    SwingOffhand,
    CriticalEffect,
    MagicalCriticalEffect
}
