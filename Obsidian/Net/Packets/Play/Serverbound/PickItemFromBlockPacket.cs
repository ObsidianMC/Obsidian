using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PickItemFromBlockPacket
{
    [Field(0), VarLength]
    public int SlotToUse { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.SlotToUse = reader.ReadVarInt();
    }
}
