using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class Explosion : IClientboundPacket
{
    [Field(0), DataFormat(typeof(float))]
    public VectorF Position { get; init; }

    [Field(1)]
    public float Strength { get; init; }

    [Field(2)]
    public ExplosionRecord[] Records { get; init; }

    [Field(3), DataFormat(typeof(float))]
    public VectorF PlayerMotion { get; init; }

    public int Id => 0x1C;
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();
}

public readonly struct ExplosionRecord
{
    public sbyte X { get; init; }
    public sbyte Y { get; init; }
    public sbyte Z { get; init; }
}
