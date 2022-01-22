using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;

public partial class LoginPluginRequest : IClientboundPacket
{
    [Field(0), VarLength]
    public int MessageId { get; set; }

    [Field(1)]
    public string Channel { get; set; }

    [Field(2)]
    public byte[] Data { get; set; }

    public int Id => 0x04;

}
