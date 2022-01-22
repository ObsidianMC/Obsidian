using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class Spectate : IServerboundPacket
{
    [Field(0)]
    public Guid TargetPlayer { get; set; }

    public int Id => 0x2D;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
