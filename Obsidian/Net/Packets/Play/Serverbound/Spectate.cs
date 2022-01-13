using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class Spectate : IServerboundPacket
{
    [Field(0)]
    public Guid TargetPlayer { get; set; }

    public int Id => 0x2D;

    public ValueTask HandleAsync(Server server, Player player)
    {
        server.Logger.LogDebug(this.AsString());
        return ValueTask.CompletedTask;
    }
}
