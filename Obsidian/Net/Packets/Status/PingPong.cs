using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Status;

public partial class PingPong : IClientboundPacket, IServerboundPacket
{
    [Field(0)]
    public long Payload { get; private set; }

    public int Id => 0x01;
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();

    public void Populate(byte[] data) => throw new NotImplementedException();

    public void Populate(MinecraftStream stream) => throw new NotImplementedException();

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
