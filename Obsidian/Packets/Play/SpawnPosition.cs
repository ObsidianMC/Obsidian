using Newtonsoft.Json;
using Obsidian.Entities;
using Obsidian.Packets.Handshaking;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class SpawnPosition : Packet
    {
        public SpawnPosition(Position location) : base(0x49, new byte[0]) => Location = location;

        public Position Location { get; private set; }

        public override async Task Populate() => this.Location = await new MemoryStream(this._packetData).ReadPositionAsync();

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MemoryStream())
            {
                await stream.WritePositionAsync(Location);
                return stream.ToArray();
            }
        }
    }
}