using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetHeldSlotPacket
{
    [Field(0)]
    public byte Slot { get; private set; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteByte(this.Slot);
    }
}
