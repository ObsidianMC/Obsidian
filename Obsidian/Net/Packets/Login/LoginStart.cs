using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;

public sealed partial class LoginStart : IServerboundPacket
{
    [Field(0)]
    public string Username { get; set; }

    [Field(1)]
    public Guid? PlayerUuid { get; set; }

    public int Id => 0x00;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
