using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class EncryptionResponse : Packet
    {
        public byte[] SharedSecret { get; private set; }

        public byte[] VerifyToken { get; set; }

        public EncryptionResponse(byte[] data) : base(0x01, data)
        {
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteVarIntAsync(this.SharedSecret.Length);
            await stream.WriteAsync(this.SharedSecret);

            await stream.WriteVarIntAsync(this.VerifyToken.Length);
            await stream.WriteAsync(this.VerifyToken);
        }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            var secretLength = await stream.ReadVarIntAsync();
            this.SharedSecret = await stream.ReadUInt8ArrayAsync(secretLength);

            var tokenLength = await stream.ReadVarIntAsync();
            this.VerifyToken = await stream.ReadUInt8ArrayAsync(tokenLength);
        }
    }
}