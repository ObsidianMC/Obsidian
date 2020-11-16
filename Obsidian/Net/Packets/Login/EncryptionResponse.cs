using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    public partial class EncryptionResponse : IPacket
    {
        [Field(0, true)]
        public byte[] SharedSecret { get; private set; }

        [Field(1, true)]
        public byte[] VerifyToken { get; set; }

        public int Id => 0x01;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;

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