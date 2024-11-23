using Obsidian.Net.WindowProperties;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ContainerSetDataPacket
{
    [Field(0)]
    public int ContainerId { get; init; }

    [Field(1)]
    public IWindowProperty WindowProperty { get; init; } = default!;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.ContainerId);

        writer.WriteShort(this.WindowProperty.Property);
        writer.WriteShort(this.WindowProperty.Value);
    }
}
