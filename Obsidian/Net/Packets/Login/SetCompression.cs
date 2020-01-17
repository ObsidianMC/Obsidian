using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    public class SetCompression : Packet
    {
        public int Threshold { get; }

        public SetCompression(int threshold) : base(0x03, System.Array.Empty<byte>())
        {
            this.Threshold = threshold;
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteVarIntAsync(this.Threshold);
        }

        protected override Task PopulateAsync(MinecraftStream stream)
        {
            return Task.CompletedTask;
        }
    }
}
