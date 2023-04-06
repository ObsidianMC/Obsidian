using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class SoundEffectPacket : IClientboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public Sounds Sound { get; init; }

    [Field(1), ActualType(typeof(int)), VarLength]
    public SoundCategory Category { get; init; }

    [Field(2)]
    public SoundPosition SoundPosition { get; init; }

    [Field(3)]
    public float Volume { get; init; }

    [Field(4)]
    public float Pitch { get; init; }

    [Field(5)]
    public long Seed { get; init; }

    public int Id => 0x62;

    public SoundEffectPacket(Sounds soundId, SoundPosition soundPosition, SoundCategory category = SoundCategory.Master, float volume = 1f, float pitch = 1f)
    {
        Sound = soundId;
        Category = category;
        SoundPosition = soundPosition;
        Volume = volume;
        Pitch = pitch;
    }
}
