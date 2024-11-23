using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ContainerButtonClickPacket
{
    [Field(0)]
    public int ContainerId { get; private set; }

    [Field(1)]
    public int ButtonId { get; private set; }

    public override ValueTask HandleAsync(Server server, Player player) => default;

    public override void Populate(INetStreamReader reader)
    {
        this.ContainerId = reader.ReadVarInt();
        this.ButtonId = reader.ReadVarInt();
    }
}
