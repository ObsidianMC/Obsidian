using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ContainerButtonClickPacket
{
    [Field(0)]
    public sbyte WindowId { get; private set; }

    [Field(1)]
    public sbyte ButtonId { get; private set; }

    public override ValueTask HandleAsync(Server server, Player player) => default;
}
