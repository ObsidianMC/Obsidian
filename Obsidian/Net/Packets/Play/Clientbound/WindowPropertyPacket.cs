using Obsidian.Net.WindowProperties;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class WindowPropertyPacket : IClientboundPacket
{
    [Field(0)]
    public byte WindowId { get; init; }

    [Field(1)]
    public IWindowProperty WindowProperty { get; init; }

    public int Id => 0x15;
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();
}
