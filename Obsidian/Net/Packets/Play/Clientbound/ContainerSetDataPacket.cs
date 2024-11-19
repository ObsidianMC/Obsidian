using Obsidian.Net.WindowProperties;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ContainerSetDataPacket
{
    [Field(0)]
    public byte WindowId { get; init; }

    [Field(1)]
    public IWindowProperty WindowProperty { get; init; }
}
