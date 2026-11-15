namespace Obsidian.API;
/// <summary>The world details sent to a player when they log in or respawn.</summary>
public readonly record struct CommonPlayerSpawnInfo : INetworkSerializable<CommonPlayerSpawnInfo>
{
    /// <summary>Gets the registry id of the dimension type the player is in.</summary>
    public required int DimensionType { get; init; }

    /// <summary>Gets the id of the level the player is in, such as <c>minecraft:overworld</c>.</summary>
    public required string DimensionName { get; init; }
    /// <summary>Gets the hashed world seed, which the client uses to blend biome colors.</summary>
    public long HashedSeed { get; init; }
    /// <summary>Gets the player's game mode.</summary>
    public GameMode GameMode { get; init; }

    /// <summary>
    /// The game mode the player had before this one, if any, which the client's game mode switcher starts from.
    /// </summary>
    public GameMode? PreviousGamemode { get; init; }

    /// <summary>Gets whether the level is a debug world.</summary>
    public bool Debug { get; init; }

    /// <summary>Gets whether the level is a superflat world, which changes how the client draws the horizon.</summary>
    public bool Flat { get; init; }

    /// <summary>
    /// Gets where the player last died, or null if they haven't. The client points recovery compasses at it.
    /// </summary>
    public DeathLocation? DeathLocation { get; init; }

    /// <summary>Gets the ticks left before the player can use a portal again.</summary>
    public int PortalCooldown { get; init; }

    /// <summary>Gets the level's sea level.</summary>
    public int SeaLevel { get; init; }

    /// <summary>Reads the details from a network stream.</summary>
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

    /// <summary>Writes the details to a network stream.</summary>
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
