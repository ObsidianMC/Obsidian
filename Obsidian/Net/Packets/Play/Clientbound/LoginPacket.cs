using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class LoginPacket : IClientboundPacket
{
    [Field(0)]
    public int EntityId { get; init; }

    [Field(1)]
    public bool Hardcore { get; init; } = false;

    [Field(2), ActualType(typeof(byte))]
    public Gamemode Gamemode { get; init; } = Gamemode.Survival;

    [Field(3)]
    public sbyte PreviousGamemode { get; init; } = 0;

    [Field(5)]
    public List<string> DimensionNames { get; init; }

    [Field(6)]
    public MixedCodec Codecs { get; init; }

    [Field(7)]
    public string DimensionType { get; init; }

    [Field(8)]
    public string DimensionName { get; init; }

    [Field(9)]
    public long HashedSeed { get; init; }

    [Field(10), VarLength]
    private const int MaxPlayers = 0;

    [Field(11), VarLength]
    public int ViewDistance { get; init; } = 32;

    [Field(12), VarLength]
    public int SimulationDistance { get; init; } = 12;

    [Field(13)]
    public bool ReducedDebugInfo { get; init; } = false;

    [Field(14)]
    public bool EnableRespawnScreen { get; init; } = true;

    [Field(15)]
    public bool Debug { get; init; } = false;

    [Field(16)]
    public bool Flat { get; init; } = false;

    [Field(17)]
    public bool HasDeathLocation { get; init; }

    [Field(18), Condition("HasDeathLocation")]
    public string DeathDimensionName { get; init; }

    [Field(19), Condition("HasDeathLocation")]
    public Vector DeathLocation { get; init; }

    [Field(20), VarLength]
    public int PortalCooldown { get; init; }

    public int Id => 0x28;
}
