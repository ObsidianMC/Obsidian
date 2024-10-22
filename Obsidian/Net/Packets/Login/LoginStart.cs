using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;

public sealed partial class LoginStart : IServerboundPacket
{
    [Field(0)]
    public string Username { get; set; }

    [Field(1), ActualType(typeof(Guid))]
    public Guid? PlayerUuid { get; set; }

    public int Id => 0x00;

    public ValueTask HandleAsync(Client client) => default;
    public ValueTask HandleAsync(Server server, Player player) => default;
}
