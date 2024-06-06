using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;
public sealed partial class PluginResponsePacket : IServerboundPacket
{
    [Field(0)]
    [VarLength]
    public int MessageID { get; private set; }

    [Field(1)]
    public bool Successful { get; private set; }

    [Field(2)]
    [Condition("Successful")]
    public byte[] Data { get; private set; }

    public int Id => 0x02;

    public ValueTask HandleAsync(Server server, Player player) => throw new NotImplementedException();
}
