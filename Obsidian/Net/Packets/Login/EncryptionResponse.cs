using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login;

public partial class EncryptionResponse : IServerboundPacket
{
    [Field(0)]
    public byte[] SharedSecret { get; private set; }

    [Field(1)]
    public byte[] VerifyToken { get; private set; }

    public int Id => 0x01;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
