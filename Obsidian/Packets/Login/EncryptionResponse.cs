using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Packets.Login
{
    public class EncryptionResponse : Packet
    {
        public byte[] SharedSecret { get; private set; }

        public byte[] VerifyToken { get; set; }

        public EncryptionResponse(byte[] data) : base(0x01, data) { }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteVarIntAsync(this.SharedSecret.Length);
                await stream.WriteUInt8ArrayAsync(this.SharedSecret);
                await stream.WriteVarIntAsync(this.VerifyToken.Length);
                await stream.WriteUInt8ArrayAsync(this.VerifyToken);

                return stream.ToArray();
            }
        }

        protected override async Task PopulateAsync()
        {
            using (var stream = new MinecraftStream(this._packetData))
            {
                var secretLength = await stream.ReadVarIntAsync();
                this.SharedSecret = await stream.ReadUInt8ArrayAsync(secretLength);

                var tokenLength = await stream.ReadVarIntAsync();
                this.VerifyToken = await stream.ReadUInt8ArrayAsync(tokenLength);
            }
        }
    }
}
