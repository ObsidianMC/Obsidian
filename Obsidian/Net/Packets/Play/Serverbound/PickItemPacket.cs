using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PickItemPacket
{
    [Field(0), VarLength]
    public int SlotToUse { get; private set; }
}
