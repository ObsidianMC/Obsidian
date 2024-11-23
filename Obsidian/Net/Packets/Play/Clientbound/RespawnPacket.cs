using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class RespawnPacket
{
    [Field(0)]
    public required string DimensionType { get; init; }

    [Field(1)]
    public required string DimensionName { get; init; }

    [Field(2)]
    public long HashedSeed { get; init; }

    [Field(3), ActualType(typeof(byte))]
    public Gamemode Gamemode { get; init; }

    [Field(4), ActualType(typeof(sbyte))]
    public Gamemode PreviousGamemode { get; init; }

    [Field(5)]
    public bool IsDebug { get; init; }

    [Field(6)]
    public bool IsFlat { get; init; }


    [Field(9)]
    public GlobalPosition? DeathPosition { get; init; }

    [Field(10), VarLength]
    public int PortalCooldown { get; init; }

    [Field(11), VarLength]
    public int SeaLevel { get; init; }

    [Field(12)]
    public DataKept DataKept { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.DimensionType);
        writer.WriteString(this.DimensionName);

        writer.WriteLong(this.HashedSeed);

        writer.WriteByte(this.Gamemode);
        writer.WriteByte(this.PreviousGamemode);

        writer.WriteBoolean(this.IsDebug);
        writer.WriteBoolean(this.IsFlat);

        writer.WriteOptional(this.DeathPosition);

        writer.WriteVarInt(this.PortalCooldown);
        writer.WriteVarInt(this.SeaLevel);

        writer.WriteByte(this.DataKept);
    }
}
