using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class LevelParticlesPacket
{
    [Field(0)]
    public bool OverrideLimiter { get; set; }

    [Field(1)]
    public bool AlwaysShow { get; init; }

    [Field(2), DataFormat(typeof(double))]
    public required VectorD Position { get; init; }

    [Field(3), DataFormat(typeof(float))]
    public VectorF Offset { get; init; }

    [Field(4)]
    public float MaxSpeed { get; init; }

    /// <summary>Per-axis speeds; when omitted, MaxSpeed applies to every axis.</summary>
    public VectorF? Speed { get; init; }

    public ParticleRandomization Randomization { get; init; }

    [Field(5)]
    public required int ParticleCount { get; init; }

    [Field(6)]
    public ParticleData Data { get; init; } = default!;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt((int)this.Data.ParticleType);
        this.Data.Write(writer);
        writer.WriteBoolean(this.OverrideLimiter);
        writer.WriteBoolean(this.AlwaysShow);
        VectorD.Write(this.Position, writer);
        writer.WriteAbsoluteFloatPositionF(this.Offset);
        writer.WriteAbsoluteFloatPositionF(this.Speed ?? new VectorF(this.MaxSpeed));
        writer.WriteVarInt(this.ParticleCount);
        writer.WriteVarInt((int)this.Randomization);
    }
}

public enum ParticleRandomization
{
    Default,
    Alternative,
    AlternativeWithSpeed
}
