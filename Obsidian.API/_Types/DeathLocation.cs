namespace Obsidian.API;
/// <summary>Where a player last died.</summary>
public readonly record struct DeathLocation : INetworkSerializable<DeathLocation>
{
    /// <summary>Gets the id of the level they died in, such as <c>minecraft:overworld</c>.</summary>
    public required string DeathDimensionName { get; init; }
    /// <summary>Gets the block position they died at.</summary>
    public required Vector Location { get; init; }

    /// <summary>Reads a location from a network stream.</summary>
    public static DeathLocation Read(INetStreamReader reader) => new()
    {
        DeathDimensionName = reader.ReadString(),
        Location = reader.ReadPosition()
    };

    /// <summary>Writes a location to a network stream.</summary>
    public static void Write(DeathLocation value, INetStreamWriter writer)
    {
        writer.WriteString(value.DeathDimensionName);
        writer.WritePosition(value.Location);
    }
}
