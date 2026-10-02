using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ExplodePacket
{
    [Field(0), DataFormat(typeof(double))]
    public required VectorF Center { get; init; }

    [Field(1)]
    public required float Radius { get; init; }

    [Field(2)]
    public int BlockCount { get; init; }

    [Field(3)]
    public Velocity? PlayerKnockback { get; init;  }

    [Field(4), ActualType(typeof(int)), VarLength]
    public required ParticleData ExplosionParticle { get; init; }

    [Field(5)]
    public required SoundEffect ExplosionSound { get; init; }

    [Field(6)]
    public required List<Weighted<ExplosionRecord>> ExplosionParticleInfo { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteAbsolutePositionF(Center);
        writer.WriteSingle(Radius);
        writer.WriteInt(BlockCount);
        writer.WriteBoolean(PlayerKnockback != null);
        if (PlayerKnockback is { } knockback)
        {
            writer.WriteDouble(knockback.X);
            writer.WriteDouble(knockback.Y);
            writer.WriteDouble(knockback.Z);
        }
        writer.WriteVarInt((int)ExplosionParticle.ParticleType);
        ExplosionParticle.Write(writer);
        writer.WriteVarInt(0);
        writer.WriteString(ExplosionSound.SoundId);
        writer.WriteOptional(ExplosionSound.FixedRange);
        writer.WriteVarInt(ExplosionParticleInfo.Count);
        foreach (var entry in ExplosionParticleInfo)
        {
            writer.WriteVarInt((int)entry.Value.Particle.ParticleType);
            entry.Value.Particle.Write(writer);
            writer.WriteSingle(entry.Value.Scaling);
            writer.WriteSingle(entry.Value.Speed);
            writer.WriteVarInt(entry.Weight);
        }
    }
}

public readonly struct ExplosionRecord
{
    public required ParticleData Particle { get; init; }

    public float Scaling { get; init; }

    public float Speed { get; init; }
}
