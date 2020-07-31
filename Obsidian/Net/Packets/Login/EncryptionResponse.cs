using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Login
{
    public class EncryptionResponse : Packet
    {
        [PacketOrder(0, true)]
        public byte[] SharedSecret { get; private set; }

        [PacketOrder(1, true)]
        public byte[] VerifyToken { get; set; }

        public EncryptionResponse() : base(0x01) { }

        public EncryptionResponse(byte[] data) : base(0x01, data) { }

        /*protected override async Task ComposeAsync(MinecraftStream stream)
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
        }*/
    }
}