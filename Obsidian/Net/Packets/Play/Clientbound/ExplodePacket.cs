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

    //TODO someone else can do this it seems like the structure hasn't change but I cba to look through it
    public override void Serialize(INetStreamWriter writer) => throw new NotImplementedException();
}

public readonly struct ExplosionRecord
{
    public required ParticleData Particle { get; init; }

    public float Scaling { get; init; }

    public float Speed { get; init; }
}
