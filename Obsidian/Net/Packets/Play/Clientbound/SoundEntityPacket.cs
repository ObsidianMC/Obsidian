using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SoundEntityPacket
{
    [Field(0)]
    public required string SoundLocation { get; init; }

    [Field(1)]
    public float? FixedRange { get; init; }

    [Field(4), ActualType(typeof(int)), VarLength]
    public required SoundCategory Category { get; init; }

    [Field(5), VarLength]
    public required int EntityId { get; init; }

    [Field(6)]
    public required float Volume { get; init; }

    [Field(7)]
    public required float Pitch { get; init; }

    [Field(8)]
    public long Seed { get; init; } = Globals.Random.Next();

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.SoundLocation);

        writer.WriteOptional(this.FixedRange);

        writer.WriteVarInt(this.Category);
        writer.WriteVarInt(this.EntityId);

        writer.WriteSingle(this.Volume);
        writer.WriteSingle(this.Pitch);
        writer.WriteLong(this.Seed);
    }
}
