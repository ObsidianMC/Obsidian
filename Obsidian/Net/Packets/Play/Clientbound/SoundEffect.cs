using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SoundEffect : IClientboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public Sounds SoundId { get; }

    [Field(1), ActualType(typeof(int)), VarLength]
    public SoundCategory Category { get; }

    [Field(2)]
    public SoundPosition Position { get; }

    [Field(3)]
    public float Volume { get; }

    [Field(4)]
    public float Pitch { get; }

    public int Id => 0x5D;
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();

    public SoundEffect(Sounds soundId, SoundPosition position, SoundCategory category = SoundCategory.Master, float volume = 1f, float pitch = 1f)
    {
        SoundId = soundId;
        Position = position;
        Category = category;
        Volume = volume;
        Pitch = pitch;
    }
}
