using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class Particle : IClientboundPacket
{
    [Field(0), ActualType(typeof(int))]
    public ParticleType Type { get; init; }

    /// <summary>
    /// If true, particle distance increases from 256 to 65536.
    /// </summary>
    [Field(1)]
    public bool LongDistance { get; init; }

    [Field(2), DataFormat(typeof(double))]
    public VectorF Position { get; init; }

    [Field(3), DataFormat(typeof(float))]
    public VectorF Offset { get; init; }

    [Field(6)]
    public float ParticleData { get; init; }

    [Field(7)]
    public int ParticleCount { get; init; }

    [Field(8)]
    public ParticleData Data { get; init; }

    public int Id => 0x24;

    public Particle(ParticleType type, VectorF position, int particleCount)
    {
        Type = type;
        Position = position;
        ParticleCount = particleCount;
    }
}
