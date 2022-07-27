using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class EntitySoundEffectPacket : IClientboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public Sounds SoundId { get; }

    [Field(1), ActualType(typeof(int)), VarLength]
    public SoundCategory Category { get; }

    [Field(2), VarLength]
    public int EntityId { get; }

    [Field(3)]
    public float Volume { get; }

    [Field(4)]
    public float Pitch { get; }

    public int Id => 0x5C;

    public EntitySoundEffectPacket(Sounds soundId, int entityId, SoundCategory category = SoundCategory.Master, float volume = 1f, float pitch = 1f)
    {
        SoundId = soundId;
        Category = category;
        EntityId = entityId;
        Volume = volume;
        Pitch = pitch;
    }
}
