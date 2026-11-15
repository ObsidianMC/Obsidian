namespace Obsidian.API;
/// <summary>A block state property and the value it must have, such as <c>facing</c> set to <c>north</c>.</summary>
public sealed record class BlockStateProperty : INetworkSerializable<BlockStateProperty>
{
    /// <summary>Gets the property's name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the property's value.</summary>
    public required string Value { get; init; }


    /// <summary>Reads a property from a network stream.</summary>
    public static BlockStateProperty Read(INetStreamReader reader) => new()
    {
        Name = reader.ReadString(),
        Value = reader.ReadString()
    };

    /// <summary>Writes a property to a network stream.</summary>
    public static void Write(BlockStateProperty value, INetStreamWriter writer)
    {
        writer.WriteString(value.Name);
        writer.WriteString(value.Value);
    }
}
