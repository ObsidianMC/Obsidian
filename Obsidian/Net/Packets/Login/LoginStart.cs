using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;

public partial class LoginStart : IServerboundPacket
{
    [Field(0)]
    public string Username { get; private set; }

    public int Id => 0x00;

    public void Populate(byte[] data) => throw new NotImplementedException();

    public void Populate(MinecraftStream stream) => throw new NotImplementedException();

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
