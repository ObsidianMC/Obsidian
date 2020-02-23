using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    public class EncryptionRequest : Packet
    {
        public string ServerId { get; private set; }

        public byte[] PublicKey { get; private set; }

        public byte[] VerifyToken { get; private set; }

        public EncryptionRequest(byte[] publicKey, byte[] verifyToken) : base(0x01, System.Array.Empty<byte>())
        {
            this.PublicKey = publicKey;

            this.VerifyToken = verifyToken;
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteStringAsync(this.ServerId ?? string.Empty);
            await stream.WriteVarIntAsync(this.PublicKey.Length);
            await stream.WriteAsync(this.PublicKey);
            await stream.WriteVarIntAsync(4);
            await stream.WriteAsync(this.VerifyToken);
        }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            this.ServerId = await stream.ReadStringAsync() ?? string.Empty;
            var keyLength = await stream.ReadVarIntAsync();
            this.PublicKey = await stream.ReadUInt8ArrayAsync(keyLength);

            var tokenLength = await stream.ReadVarIntAsync();
            this.VerifyToken = await stream.ReadUInt8ArrayAsync(tokenLength);
        }
    }
}