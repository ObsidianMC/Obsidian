using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class AnimatePacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1), ActualType(typeof(byte))]
    public EntityAnimationType Animation { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.EntityId);
        writer.WriteByte(this.Animation);
    }
}

/// <summary>
/// The animations <see cref="AnimatePacket"/> plays. Since 26.3 arm swings go through <see cref="SwingAnimationPacket"/>.
/// </summary>
public enum EntityAnimationType : byte
{
    WakeUp,
    CriticalEffect,
    MagicalCriticalEffect
}
