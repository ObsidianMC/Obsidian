using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class SpawnPosition : Packet
    {
        public SpawnPosition(Position location) : base(0x49, new byte[0]) => Location = location;

        public Position Location { get; private set; }

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