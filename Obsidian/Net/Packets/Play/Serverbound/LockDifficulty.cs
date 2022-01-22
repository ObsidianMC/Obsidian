using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class LockDifficulty : IServerboundPacket
{
    [Field(0)]
    public bool Locked { get; set; }

    public int Id => 0x10;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
