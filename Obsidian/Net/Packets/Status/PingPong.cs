using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Status;

public partial class PingPong : IClientboundPacket, IServerboundPacket
{
    [Field(0)]
    public long Payload { get; private set; }

    public int Id => 0x01;

    public ValueTask HandleAsync(Client client) => default;
    public ValueTask HandleAsync(Server server, Player player) => default;
}
