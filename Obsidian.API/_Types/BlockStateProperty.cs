namespace Obsidian.API;
public sealed record class BlockStateProperty : INetworkSerializable<BlockStateProperty>
{
    public required string Name { get; set; }
    public required string Value { get; set; }


    public static BlockStateProperty Read(INetStreamReader reader) => new()
    {
        Name = reader.ReadString(),
        Value = reader.ReadString()
    };

    public static void Write(BlockStateProperty value, INetStreamWriter writer)
    {
        writer.WriteString(value.Name);
        writer.WriteString(value.Value);
    }
}
