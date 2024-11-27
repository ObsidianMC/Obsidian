namespace Obsidian.API;
public readonly struct GlobalPosition : INetworkSerializable<GlobalPosition>
{
    public required string DimensionName { get; init; }

    public required Vector Position { get; init; }

    public static void Write(GlobalPosition value, INetStreamWriter writer)
    {
        writer.WriteString(value.DimensionName);
        writer.WritePosition(value.Position);
    }
}
