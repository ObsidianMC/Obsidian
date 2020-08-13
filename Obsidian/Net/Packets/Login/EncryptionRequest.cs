using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;

namespace Obsidian.Net.Packets.Login
{
    public class EncryptionRequest : Packet
    {
        [Field(0)]
        public string ServerId { get; private set; } = string.Empty;

        [Field(1, Type = DataType.VarInt)]
        public int KeyLength { get; private set; }

        [Field(2)]
        public byte[] PublicKey { get; private set; }

        [Field(3, Type = DataType.VarInt)]
        public int TokenLength = 4;

        [Field(4)]
        public byte[] VerifyToken { get; private set; }

        public EncryptionRequest(byte[] publicKey, byte[] verifyToken) : base(0x01)
        {
            this.PublicKey = publicKey;
            this.KeyLength = publicKey.Length;

            this.VerifyToken = verifyToken;
        }

        /*protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteStringAsync(this.ServerId);
            await stream.WriteVarIntAsync(this.PublicKey.Length);
            await stream.WriteAsync(this.PublicKey);
            await stream.WriteVarIntAsync(this.TokenLength);
            await stream.WriteAsync(this.VerifyToken);
        }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            this.ServerId = await stream.ReadStringAsync() ?? string.Empty;
            this.KeyLength = await stream.ReadVarIntAsync();
            this.PublicKey = await stream.ReadUInt8ArrayAsync(this.KeyLength);

            this.TokenLength = await stream.ReadVarIntAsync();
            this.VerifyToken = await stream.ReadUInt8ArrayAsync(this.TokenLength);
        }*/
    }
}