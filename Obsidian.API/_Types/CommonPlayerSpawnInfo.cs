namespace Obsidian.API;
public readonly record struct CommonPlayerSpawnInfo : INetworkSerializable<CommonPlayerSpawnInfo>
{
    public required int DimensionType { get; init; }

    public required string DimensionName { get; init; }
    public long HashedSeed { get; init; }
    public GameMode GameMode { get; init; }

    /// <summary>
    /// The game mode the player had before this one, if any, which the client's game mode switcher starts from.
    /// </summary>
    public GameMode? PreviousGamemode { get; init; }

    public bool Debug { get; init; }

    public bool Flat { get; init; }

    public DeathLocation? DeathLocation { get; init; }

    public int PortalCooldown { get; init; }

    public int SeaLevel { get; init; }

    public static CommonPlayerSpawnInfo Read(INetStreamReader reader) => new()
    {
        DimensionType = reader.ReadVarInt(),
        DimensionName = reader.ReadString(),

        HashedSeed = reader.ReadLong(),
        
        GameMode = (GameMode)reader.ReadVarInt(),
        PreviousGamemode = reader.ReadVarInt() is var previous and > 0 ? (GameMode)(previous - 1) : null,

        Debug = reader.ReadBoolean(),
        Flat = reader.ReadBoolean(),
        DeathLocation = reader.ReadOptional<DeathLocation>(),

        PortalCooldown = reader.ReadVarInt(),
        SeaLevel = reader.ReadVarInt()
    };

    public static void Write(CommonPlayerSpawnInfo value, INetStreamWriter writer)
    {
        writer.WriteVarInt(value.DimensionType);
        writer.WriteString(value.DimensionName);

        writer.WriteLong(value.HashedSeed);

        // Since 26.3, game modes are VarInts, and the previous one is an optional VarInt: its id plus one, or zero.
        writer.WriteVarInt((int)value.GameMode);
        writer.WriteVarInt(value.PreviousGamemode is GameMode previous ? (int)previous + 1 : 0);

        writer.WriteBoolean(value.Debug);
        writer.WriteBoolean(value.Flat);

        writer.WriteOptional(value.DeathLocation);

        writer.WriteVarInt(value.PortalCooldown);
        writer.WriteVarInt(value.SeaLevel);
    }
}
