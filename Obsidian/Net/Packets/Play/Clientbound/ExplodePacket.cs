using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ExplodePacket
{
    [Field(0), DataFormat(typeof(double))]
    public required VectorF Position { get; init; }

    [Field(1)]
    public required float Strength { get; init; }

    [Field(2)]
    public required ExplosionRecord[] Records { get; init; }

    [Field(3)]
    public required Velocity PlayerMotion { get; init; }

    [Field(4), ActualType(typeof(int)), VarLength]
    public required BlockInteraction BlockInteraction { get; init; }

    [Field(5), ActualType(typeof(int)), VarLength]
    public required ParticleType SmallExplosionParticle { get; init; }

    [Field(6)]
    //TODO SOME PARTICLES HAVE ADDITIONAL DATA IMPLEMENT THIS SOMEHOW??????
    public required object SmallExplosionParticleData { get; init; }

    [Field(7), ActualType(typeof(int)), VarLength]
    public required ParticleType LargeExplisionParticle { get; init; }

    [Field(8)]
    //TODO SOME PARTICLES HAVE ADDITIONAL DATA IMPLEMENT THIS SOMEHOW??????
    public required object LargeExplosionParticleData { get; init; }

    [Field(9)]
    public required SoundEffect ExplosionSound { get; init; }

    //TODO someone else can do this it seems like the structure hasn't change but I cba to look through it
    public override void Serialize(INetStreamWriter writer) => throw new NotImplementedException();
}

public readonly struct ExplosionRecord
{
    public sbyte X { get; init; }
    public sbyte Y { get; init; }
    public sbyte Z { get; init; }
}
