namespace Obsidian.API;
public readonly struct CommonPlayerSpawnInfo : INetworkSerializable<CommonPlayerSpawnInfo>
{
    public required int DimensionType { get; init; }

    public required string DimensionName { get; init; }
    public long HashedSeed { get; init; }
    public Gamemode Gamemode { get; init; }
    public Gamemode PreviousGamemode { get; init; }

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
        
        Gamemode = reader.ReadUnsignedByte<Gamemode>(),
        PreviousGamemode = reader.ReadUnsignedByte<Gamemode>(),

        Debug = reader.ReadBoolean(),
        Flat = reader.ReadBoolean(),
        DeathLocation = reader.ReadOptional<DeathLocation>()
    };

    public static void Write(CommonPlayerSpawnInfo value, INetStreamWriter writer)
    {
        writer.WriteVarInt(value.DimensionType);
        writer.WriteString(value.DimensionName);

        writer.WriteLong(value.HashedSeed);

        writer.WriteByte(value.Gamemode);
        writer.WriteByte(value.PreviousGamemode);

        writer.WriteBoolean(value.Debug);
        writer.WriteBoolean(value.Flat);

        writer.WriteOptional(value.DeathLocation);

        writer.WriteVarInt(value.PortalCooldown);
        writer.WriteVarInt(value.SeaLevel);
    }
}
