using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;
public sealed partial class PluginRequestPacket : IClientboundPacket
{
    [Field(0)]
    [VarLength]
    public required int MessageID { get; init; }

    [Field(1)]
    public required string Channel { get; init; }

    [Field(2)]
    public required byte[] Data { get; init; }

    public int Id => 0x04;
}
