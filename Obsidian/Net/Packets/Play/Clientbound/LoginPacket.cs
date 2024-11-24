using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class LoginPacket
{
    [Field(0)]
    public int EntityId { get; init; }

    [Field(1)]
    public bool Hardcore { get; init; } = false;

    [Field(2)]
    public List<string> DimensionNames { get; init; }

    [Field(3), VarLength]
    private const int MaxPlayers = 0;

    [Field(4), VarLength]
    public int ViewDistance { get; init; } = 32;

    [Field(5), VarLength]
    public int SimulationDistance { get; init; } = 12;

    [Field(6)]
    public bool ReducedDebugInfo { get; init; } = false;

    [Field(7)]
    public bool EnableRespawnScreen { get; init; } = true;

    [Field(8)]
    public bool DoLimitedCrafting { get; init; } = false;

    [Field(9), VarLength]
    public int DimensionType { get; init; }

    [Field(10)]
    public string DimensionName { get; init; }

    [Field(11)]
    public long HashedSeed { get; init; }

    [Field(12), ActualType(typeof(byte))]
    public Gamemode Gamemode { get; init; } = Gamemode.Survival;

    [Field(13), ActualType(typeof(sbyte))]
    public Gamemode PreviousGamemode { get; init; } = Gamemode.Survival;

    [Field(14)]
    public bool Debug { get; init; } = false;

    [Field(15)]
    public bool Flat { get; init; } = false;

    [Field(16)]
    public bool HasDeathLocation { get; init; }

    [Field(17), Condition("HasDeathLocation")]
    public string? DeathDimensionName { get; init; }

    [Field(18), Condition("HasDeathLocation")]
    public Vector? DeathLocation { get; init; }

    [Field(19), VarLength]
    public int PortalCooldown { get; init; }

    [Field(20), VarLength]
    public int SeaLevel { get; init; }

    [Field(21)]
    public bool EnforcesSecureChat { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteInt(this.EntityId);
        writer.WriteBoolean(this.Hardcore);

        writer.WriteVarInt(this.DimensionNames.Count);
        foreach(var dimName in this.DimensionNames)
            writer.WriteString(dimName);

        writer.WriteVarInt(MaxPlayers);
        writer.WriteVarInt(this.ViewDistance);
        writer.WriteVarInt(this.SimulationDistance);

        writer.WriteBoolean(this.ReducedDebugInfo);
        writer.WriteBoolean(this.EnableRespawnScreen);
        writer.WriteBoolean(this.DoLimitedCrafting);

        writer.WriteVarInt(this.DimensionType);
        writer.WriteString(this.DimensionName);

        writer.WriteLong(this.HashedSeed);

        writer.WriteByte(this.Gamemode);
        writer.WriteByte(this.PreviousGamemode);

        writer.WriteBoolean(this.Debug);
        writer.WriteBoolean(this.Flat);
        writer.WriteBoolean(this.HasDeathLocation);

        if (this.HasDeathLocation)
        {
            writer.WriteString(this.DeathDimensionName!);
            writer.WritePosition(this.DeathLocation!.Value);
        }

        writer.WriteVarInt(this.PortalCooldown);
        writer.WriteVarInt(this.SeaLevel);

        writer.WriteBoolean(this.EnforcesSecureChat);
    }
}
