using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ContainerClosePacket
{
    [Field(0)]
    public byte WindowId { get; private set; }
}
