using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;

public partial class EncryptionResponse : IServerboundPacket
{
    [Field(0)]
    public byte[] SharedSecret { get; private set; }

    [Field(1)]
    public byte[] VerifyToken { get; private set; }

    public int Id => 0x01;

    public void Populate(byte[] data) => throw new NotImplementedException();

    public void Populate(MinecraftStream stream) => throw new NotImplementedException();

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
