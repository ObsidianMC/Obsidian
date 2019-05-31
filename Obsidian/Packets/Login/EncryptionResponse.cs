using Obsidian.Logging;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets.Login
{
    public class EncryptionResponse : Packet
    {
        public byte[] SharedSecret { get; private set; }

        public byte[] VerifyToken { get; set; }

        public EncryptionResponse(byte[] data) : base(0x01, data) { }

        private readonly Logger _logger = new Logger("Encription Response: ");

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MemoryStream())
            {
                try
                {
                    await stream.WriteVarIntAsync(this.SharedSecret.Length);
                    await stream.WriteUInt8ArrayAsync(this.SharedSecret);
                    await stream.WriteVarIntAsync(this.VerifyToken.Length);
                    await stream.WriteUInt8ArrayAsync(this.VerifyToken);

                    return stream.ToArray();
                }
                catch
                {
                    throw;
                }
            }
        }

        protected override async Task PopulateAsync()
        {
            using (var stream = new MemoryStream(this._packetData))
            {
                try
                {
                    var secretLength = await stream.ReadVarIntAsync();
                    this.SharedSecret = await stream.ReadUInt8ArrayAsync(secretLength);
            
                    var tokenLength = await stream.ReadVarIntAsync();
                    this.VerifyToken = await stream.ReadUInt8ArrayAsync(tokenLength);
                }
                catch
                {
                    throw;
                }
            }
        }
    }
}
