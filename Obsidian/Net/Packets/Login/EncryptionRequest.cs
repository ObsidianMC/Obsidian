using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    [ClientOnly]
    public partial class EncryptionRequest : IClientboundPacket
    {
        [Field(0)]
        public string ServerId { get; } = string.Empty;

        [Field(1)]
        public byte[] PublicKey { get; }

        [Field(2)]
        public byte[] VerifyToken { get; }

        public int Id => 0x01;

        public EncryptionRequest(byte[] publicKey, byte[] verifyToken)
        {
            PublicKey = publicKey;
            VerifyToken = verifyToken;
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}