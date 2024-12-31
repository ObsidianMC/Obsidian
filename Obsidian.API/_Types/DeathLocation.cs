namespace Obsidian.API;
public readonly record struct DeathLocation : INetworkSerializable<DeathLocation>
{
    public required string DeathDimensionName { get; init; }
    public required Vector Location { get; init; }

    public static DeathLocation Read(INetStreamReader reader) => new()
    {
        DeathDimensionName = reader.ReadString(),
        Location = reader.ReadPosition()
    };

    public static void Write(DeathLocation value, INetStreamWriter writer)
    {
        writer.WriteString(value.DeathDimensionName);
        writer.WritePosition(value.Location);
    }
}
