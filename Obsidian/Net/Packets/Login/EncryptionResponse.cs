using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class EncryptionResponse : Packet
    {
        [Variable]
        public int SecretLength { get; set; }

        [Variable(VariableType.Array)]
        public byte[] SharedSecret { get; private set; }

        [Variable]
        public int TokenLength { get; set; }

        [Variable(VariableType.Array)]
        public byte[] VerifyToken { get; set; }

        public EncryptionResponse(byte[] data) : base(0x01, data)
        {
        }

        public override async Task DeserializeAsync()
        {
            using (var stream = new MinecraftStream(this.PacketData))
            {
                var secretLength = await stream.ReadVarIntAsync();
                this.SharedSecret = await stream.ReadUInt8ArrayAsync(secretLength);

                var tokenLength = await stream.ReadVarIntAsync();
                this.VerifyToken = await stream.ReadUInt8ArrayAsync(tokenLength);
            }
        }
    }
}