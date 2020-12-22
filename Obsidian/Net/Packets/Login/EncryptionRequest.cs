using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    public partial class EncryptionRequest : IPacket
    {
        [Field(0)]
        public string ServerId { get; private set; } = string.Empty;

        [Field(1)]
        public byte[] PublicKey { get; private set; }

        [Field(2)]
        public byte[] VerifyToken { get; private set; }

        public int Id => 0x01;

        private EncryptionRequest()
        {
        }

        public EncryptionRequest(byte[] publicKey, byte[] verifyToken) 
        {
            this.PublicKey = publicKey;
            this.VerifyToken = verifyToken;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}