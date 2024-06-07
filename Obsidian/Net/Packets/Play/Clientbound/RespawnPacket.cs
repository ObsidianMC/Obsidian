using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class RespawnPacket : IClientboundPacket
{
    [Field(0)]
    public string DimensionType { get; init; }

    [Field(1)]
    public string DimensionName { get; init; }

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

    [Field(7)]
    public bool HasDeathLocation { get; init; }

    [Field(8), Condition(nameof(HasDeathLocation))]
    public string DeathDimensionName { get; init; }

    [Field(9), Condition(nameof(HasDeathLocation))]
    public VectorF DeathLocation { get; init; }

    [Field(10), VarLength]
    public int PortalCooldown { get; init; }

    /// <summary>
    /// In the Notchian implementation, this is context dependent:<br/>
    /// <br/>
    /// normal respawns(after death) keep no data;<br/>
    /// exiting the end poem/credits keeps the attributes;<br/>
    /// other dimension changes(portals or teleports) keep all data.
    /// </summary>
    [Field(11), ActualType(typeof(sbyte))]
    public DataKept DataKept { get; init; }

    public int Id => 0x47;
}
