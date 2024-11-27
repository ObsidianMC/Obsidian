using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class SoundPacket
{
    [Field(0)]
    public required string SoundLocation { get; init; }

    [Field(3)]
    public float? FixedRange { get; init; }

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

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.SoundLocation);

        writer.WriteOptional(this.FixedRange);

        writer.WriteVarInt(this.Category);

        writer.WritePosition(this.SoundPosition);

        writer.WriteFloat(this.Volume);
        writer.WriteFloat(this.Pitch);
        writer.WriteLong(this.Seed);
    }
}
