using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class LoginPacket
{
    [Field(0)]
    public required int EntityId { get; init; }

    [Field(1)]
    public bool Hardcore { get; init; } = false;

    [Field(2)]
    public required List<string> DimensionNames { get; init; }

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

    [Field(9)]
    public required CommonPlayerSpawnInfo CommonPlayerSpawnInfo { get; init; }

    [Field(10)]
    public bool EnforcesSecureChat { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteInt(this.EntityId);
        writer.WriteBoolean(this.Hardcore);

        writer.WriteVarInt(this.DimensionNames.Count);
        foreach (var dimName in this.DimensionNames)
            writer.WriteString(dimName);

        writer.WriteVarInt(MaxPlayers);
        writer.WriteVarInt(this.ViewDistance);
        writer.WriteVarInt(this.SimulationDistance);

        writer.WriteBoolean(this.ReducedDebugInfo);
        writer.WriteBoolean(this.EnableRespawnScreen);
        writer.WriteBoolean(this.DoLimitedCrafting);

        CommonPlayerSpawnInfo.Write(this.CommonPlayerSpawnInfo, writer);

        writer.WriteBoolean(this.EnforcesSecureChat);
    }
}
