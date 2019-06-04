using Obsidian.Entities;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class SpawnPosition : Packet
    {
        public SpawnPosition(Location location) : base(0x49, new byte[0]) => Location = location;

        public Location Location { get; private set; }

        protected override async Task PopulateAsync() => this.Location = await new MinecraftStream(this._packetData).ReadPositionAsync();

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WritePositionAsync(Location);
                return stream.ToArray();
            }
        }
    }
}