using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class LookAtPacket : IClientboundPacket
{
    [Field(0), VarLength, ActualType(typeof(int))]
    public AimType Aim { get; set; }

    [Field(1), DataFormat(typeof(double))]
    public VectorF Target { get; set; }

    [Field(2)]
    public bool IsEntity { get; set; }

    [Field(3), VarLength, Condition(nameof(IsEntity))]
    public int EntityId { get; set; }

    [Field(4), VarLength, ActualType(typeof(int)), Condition(nameof(IsEntity))]
    public AimType AimEntity { get; set; }

    public int Id => 0x3F;
}

public enum AimType : int
{
    Feet = 0,
    Eyes
}
