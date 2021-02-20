using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    [ServerOnly]
    public partial class EncryptionResponse : IPacket
    {
        [Field(0)]
        public byte[] SharedSecret { get; private set; }

        [Field(1)]
        public byte[] VerifyToken { get; private set; }

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