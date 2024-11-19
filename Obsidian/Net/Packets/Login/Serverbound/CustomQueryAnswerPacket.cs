using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Serverbound;
public partial class CustomQueryAnswerPacket
{
    [Field(0)]
    [VarLength]
    public int MessageID { get; private set; }

    [Field(1)]
    public bool Successful { get; private set; }

    [Field(2)]
    [Condition("Successful")]
    public byte[] Data { get; private set; }
}
