using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SteerBoat : IServerboundPacket
{
    [Field(0)]
    public bool LeftPaddleTurning { get; set; }
    [Field(1)]
    public bool RightPaddleTurning { get; set; }

    public int Id => 0x16;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
