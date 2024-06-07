using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PickItemPacket : IServerboundPacket
{
    [Field(0), VarLength]
    public int SlotToUse { get; private set; }

    public int Id => 0x20;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
