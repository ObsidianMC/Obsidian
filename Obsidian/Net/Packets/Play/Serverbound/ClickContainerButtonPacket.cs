using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ClickContainerButtonPacket : IServerboundPacket
{
    [Field(0)]
    public sbyte WindowId { get; private set; }

    [Field(1)]
    public sbyte ButtonId { get; private set; }

    public int Id => 0x0D;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
