using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SelectTrade : IServerboundPacket
{
    [Field(0), VarLength]
    public int SelectedSlot { get; set; }

    public int Id => 0x23;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
