using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class SoundEffectPacket : IClientboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public required SoundId SoundId { get; init; }

    [Field(1), Condition("SoundId == SoundId.None")]
    public string? SoundName { get; init; }

    [Field(2), Condition("SoundId == SoundId.None")]
    public bool HasFixedRange { get; init; }

    [Field(3), Condition("SoundId == SoundId.None && HasFixedRange")]
    public float Range { get; init; }

    [Field(4), ActualType(typeof(int)), VarLength]
    public required SoundCategory Category { get; init; }

    [Field(5)]
    public required SoundPosition SoundPosition { get; init; }

    [Field(6)]
    public required float Volume { get; init; }

    [Field(7)]
    public required float Pitch { get; init; }

    [Field(8)]
    public long Seed { get; init; }

    public int Id => 0x68;
}
