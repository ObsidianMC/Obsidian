using Newtonsoft.Json;
using Obsidian.Entities;
using Obsidian.Packets.Handshaking;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class SpawnPosition
    {
        public SpawnPosition(Position location) => Location = location;

        public Position Location { get; private set; }

        public static async Task<SpawnPosition> FromArrayAsync(byte[] data) => new SpawnPosition(await new MemoryStream(data).ReadPositionAsync());

        public async Task<byte[]> ToArrayAsync()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                await stream.WritePositionAsync(Location);
                return stream.ToArray();
            }
        }
    }
}