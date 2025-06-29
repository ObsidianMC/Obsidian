using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class LevelParticlesPacket
{
    [Field(0)]
    public bool OverrideLimiter { get; set; }

    [Field(1), DataFormat(typeof(double))]
    public required VectorF Position { get; init; }

    [Field(2), DataFormat(typeof(float))]
    public VectorF Offset { get; init; }

    [Field(3)]
    public float MaxSpeed { get; init; }

    [Field(4)]
    public required int ParticleCount { get; init; }

    [Field(5)]
    public ParticleData Data { get; init; } = default!;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteBoolean(this.OverrideLimiter);
        writer.WriteAbsolutePositionF(this.Position);
        writer.WriteAbsoluteFloatPositionF(this.Offset);
        writer.WriteSingle(this.MaxSpeed);
        writer.WriteInt(this.ParticleCount);

        writer.WriteVarInt((int)this.Data.ParticleType);
        this.Data.Write(writer);
    }
}
