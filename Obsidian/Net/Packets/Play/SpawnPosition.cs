using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class SpawnPosition : Packet
    {
        public SpawnPosition(Position location) : base(0x49, System.Array.Empty<byte>()) => Location = location;

        public Position Location { get; private set; }

        protected override async Task PopulateAsync(MinecraftStream stream) => this.Location = await stream.ReadPositionAsync();

        protected override async Task ComposeAsync(MinecraftStream stream) => await stream.WritePositionAsync(this.Location);
    }
}